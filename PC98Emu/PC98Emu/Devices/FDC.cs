using System;
using PC98Emu.Bus;
using PC98Emu.Disk;

namespace PC98Emu.Devices;

/// <summary>
/// uPD765A Floppy Disk Controller for PC-98.
/// Ports: 0x90 (MSR), 0x92 (data register).
/// </summary>
public class FDC : IDevice
{
    private readonly DMA _dma;
    private readonly DiskManager _diskManager;
    private readonly Action _raiseIrq;
    private readonly byte[] _memory;

    // State machine phases
    private enum Phase { Idle, Command, Execution, Result }
    private Phase _phase;

    // MSR bits
    private const byte MSR_RQM = 0x80; // Request for Master
    private const byte MSR_DIO = 0x40; // Data Input/Output (1=FDC->CPU)
    private const byte MSR_CB  = 0x10; // Controller Busy
    private const byte MSR_NDM = 0x20; // Non-DMA Mode

    // Command buffer
    private readonly byte[] _commandBuffer = new byte[16];
    private int _commandLength;
    private int _commandExpected;
    private byte _currentCommand;

    // Result buffer
    private readonly byte[] _resultBuffer = new byte[8];
    private int _resultLength;
    private int _resultIndex;

    // Drive state
    private readonly int[] _currentCylinder = new int[4];
    private byte _st0, _st1, _st2;

    // Pending interrupt
    private bool _interruptPending;
    private byte _interruptST0;
    private byte _interruptCylinder;

    public FDC(DMA dma, DiskManager diskManager, Action raiseIrq, byte[] memory)
    {
        _dma = dma;
        _diskManager = diskManager;
        _raiseIrq = raiseIrq;
        _memory = memory;
        _phase = Phase.Idle;
    }

    public byte ReadByte(int port)
    {
        if (port == 0x90)
        {
            return GetMSR();
        }
        else if (port == 0x92)
        {
            return ReadDataRegister();
        }
        return 0xFF;
    }

    public void WriteByte(int port, byte value)
    {
        if (port == 0x92)
        {
            WriteDataRegister(value);
        }
    }

    private byte GetMSR()
    {
        byte msr = MSR_RQM; // Always ready for data

        if (_phase == Phase.Result)
        {
            msr |= MSR_DIO | MSR_CB; // FDC -> CPU, busy
        }
        else if (_phase == Phase.Command)
        {
            msr |= MSR_CB; // Busy, CPU -> FDC
        }

        return msr;
    }

    private void WriteDataRegister(byte value)
    {
        if (_phase == Phase.Idle || (_phase == Phase.Command && _commandLength == 0))
        {
            // First byte is command
            _phase = Phase.Command;
            _commandLength = 0;
            _currentCommand = (byte)(value & 0x1F); // Mask MT/MF/SK bits
            _commandBuffer[_commandLength++] = value;
            _commandExpected = GetCommandParameterCount(_currentCommand);
            if (_commandExpected == 0)
            {
                ExecuteCommand();
            }
            return;
        }

        if (_phase == Phase.Command)
        {
            _commandBuffer[_commandLength++] = value;
            if (_commandLength >= _commandExpected + 1) // +1 for command byte
            {
                ExecuteCommand();
            }
        }
    }

    private byte ReadDataRegister()
    {
        if (_phase == Phase.Result && _resultIndex < _resultLength)
        {
            byte val = _resultBuffer[_resultIndex++];
            if (_resultIndex >= _resultLength)
            {
                _phase = Phase.Idle;
                _resultIndex = 0;
                _resultLength = 0;
            }
            return val;
        }
        return 0xFF;
    }

    private int GetCommandParameterCount(byte cmd)
    {
        return cmd switch
        {
            0x06 => 8, // READ DATA
            0x05 => 8, // WRITE DATA
            0x0A => 1, // READ ID
            0x0F => 2, // SEEK
            0x07 => 1, // RECALIBRATE
            0x08 => 0, // SENSE INTERRUPT STATUS
            _ => 0
        };
    }

    private void ExecuteCommand()
    {
        switch (_currentCommand)
        {
            case 0x06: ExecuteReadData(); break;
            case 0x05: ExecuteWriteData(); break;
            case 0x0A: ExecuteReadId(); break;
            case 0x0F: ExecuteSeek(); break;
            case 0x07: ExecuteRecalibrate(); break;
            case 0x08: ExecuteSenseInterruptStatus(); break;
            default:
                // Unknown command - go idle
                _phase = Phase.Idle;
                break;
        }
    }

    private void ExecuteReadData()
    {
        // Parameters: HD|US, C, H, R, N, EOT, GPL, DTL
        int driveUnit = _commandBuffer[1] & 0x03;
        int cylinder = _commandBuffer[2];
        int head = _commandBuffer[3];
        int sectorStart = _commandBuffer[4];
        int sizeCode = _commandBuffer[5];
        int eot = _commandBuffer[6];
        int sectorSize = 128 << sizeCode;

        var disk = _diskManager.GetFloppy(driveUnit);

        _st0 = (byte)(driveUnit | (head << 2));
        _st1 = 0;
        _st2 = 0;

        if (disk == null)
        {
            // No disk - set error
            _st0 |= 0x40; // Abnormal termination
            _st1 |= 0x01; // Missing address mark
            SetReadWriteResult(cylinder, head, sectorStart, sizeCode);
            return;
        }

        // Read sectors from sectorStart to EOT
        _phase = Phase.Execution;
        bool success = true;

        for (int s = sectorStart; s <= eot; s++)
        {
            var buffer = new byte[sectorSize];
            if (disk.ReadSector(cylinder, head, s, buffer))
            {
                _dma.TransferToMemory(2, buffer, _memory);
            }
            else
            {
                _st0 |= 0x40;
                _st1 |= 0x04; // No data
                success = false;
                break;
            }
        }

        if (success)
        {
            _st0 &= 0x3F; // Normal termination
        }

        _interruptPending = true;
        _interruptST0 = _st0;
        _interruptCylinder = (byte)cylinder;

        SetReadWriteResult(cylinder, head, eot + 1, sizeCode);
        _raiseIrq();
    }

    private void ExecuteWriteData()
    {
        int driveUnit = _commandBuffer[1] & 0x03;
        int cylinder = _commandBuffer[2];
        int head = _commandBuffer[3];
        int sectorStart = _commandBuffer[4];
        int sizeCode = _commandBuffer[5];
        int eot = _commandBuffer[6];
        int sectorSize = 128 << sizeCode;

        var disk = _diskManager.GetFloppy(driveUnit);

        _st0 = (byte)(driveUnit | (head << 2));
        _st1 = 0;
        _st2 = 0;

        if (disk == null)
        {
            _st0 |= 0x40;
            _st1 |= 0x01;
            SetReadWriteResult(cylinder, head, sectorStart, sizeCode);
            return;
        }

        _phase = Phase.Execution;

        // Read from DMA and write to disk
        for (int s = sectorStart; s <= eot; s++)
        {
            var buffer = new byte[sectorSize];
            _dma.TransferFromMemory(2, buffer, _memory);

            if (!disk.WriteSector(cylinder, head, s, buffer))
            {
                _st0 |= 0x40;
                _st1 |= 0x04;
                break;
            }
        }

        _interruptPending = true;
        _interruptST0 = _st0;
        _interruptCylinder = (byte)cylinder;

        SetReadWriteResult(cylinder, head, eot + 1, sizeCode);
        _raiseIrq();
    }

    private void SetReadWriteResult(int cylinder, int head, int sector, int sizeCode)
    {
        _resultBuffer[0] = _st0;
        _resultBuffer[1] = _st1;
        _resultBuffer[2] = _st2;
        _resultBuffer[3] = (byte)cylinder;
        _resultBuffer[4] = (byte)head;
        _resultBuffer[5] = (byte)sector;
        _resultBuffer[6] = (byte)sizeCode;
        _resultLength = 7;
        _resultIndex = 0;
        _phase = Phase.Result;
    }

    private void ExecuteReadId()
    {
        int driveUnit = _commandBuffer[1] & 0x03;
        int head = (_commandBuffer[1] >> 2) & 0x01;
        int cyl = _currentCylinder[driveUnit];

        _st0 = (byte)(driveUnit | (head << 2));
        _st1 = 0;
        _st2 = 0;

        // Derive size code from disk's actual sector size
        int sizeCode = 2; // default 512 bytes
        var disk = _diskManager.GetFloppy(driveUnit);
        if (disk != null)
        {
            int sz = disk.SectorSize;
            sizeCode = 0;
            while (sz > 128) { sz >>= 1; sizeCode++; }
        }

        _resultBuffer[0] = _st0;
        _resultBuffer[1] = _st1;
        _resultBuffer[2] = _st2;
        _resultBuffer[3] = (byte)cyl;
        _resultBuffer[4] = (byte)head;
        _resultBuffer[5] = 1; // Sector 1
        _resultBuffer[6] = (byte)sizeCode;
        _resultLength = 7;
        _resultIndex = 0;
        _phase = Phase.Result;

        _raiseIrq();
    }

    private void ExecuteSeek()
    {
        int driveUnit = _commandBuffer[1] & 0x03;
        int newCylinder = _commandBuffer[2];
        _currentCylinder[driveUnit] = newCylinder;

        _interruptPending = true;
        _interruptST0 = (byte)(0x20 | driveUnit); // Seek end
        _interruptCylinder = (byte)newCylinder;

        _phase = Phase.Idle;
        _raiseIrq();
    }

    private void ExecuteRecalibrate()
    {
        int driveUnit = _commandBuffer[1] & 0x03;
        _currentCylinder[driveUnit] = 0;

        _interruptPending = true;
        _interruptST0 = (byte)(0x20 | driveUnit); // Seek end
        _interruptCylinder = 0;

        _phase = Phase.Idle;
        _raiseIrq();
    }

    private void ExecuteSenseInterruptStatus()
    {
        if (_interruptPending)
        {
            _resultBuffer[0] = _interruptST0;
            _resultBuffer[1] = _interruptCylinder;
            _resultLength = 2;
            _interruptPending = false;
        }
        else
        {
            _resultBuffer[0] = 0x80; // Invalid command
            _resultLength = 1;
        }
        _resultIndex = 0;
        _phase = Phase.Result;
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);

    public void Reset()
    {
        _phase = Phase.Idle;
        _commandLength = 0;
        _resultLength = 0;
        _resultIndex = 0;
        _st0 = _st1 = _st2 = 0;
        _interruptPending = false;
        for (int i = 0; i < 4; i++) _currentCylinder[i] = 0;
    }

    public void Tick(int cycles) { }

    public int[] GetPortRange() => new[] { 0x90, 0x92 };
}

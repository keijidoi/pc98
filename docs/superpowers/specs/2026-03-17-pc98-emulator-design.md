# PC-98 エミュレータ設計書

## 概要

C#で実装するPC-98互換エミュレータ。できるだけ多くのPC-98ソフトウェアを動作させることを目標とする。SDL2を使用した描画・音声・入力処理、独自互換BIOSによるROM不要の動作を実現する。

## スコープ

- **対象**: NEC PC-9801VM以降（V30 CPU、リアルモードのみ）
- **対象外**: 286/386プロテクトモード、EGC（Enhanced Graphic Charger）は将来の拡張とする
- **未マッピングI/Oポートへのアクセス**: ログ出力して0xFFを返す（サイレント無視）
- **未マッピングメモリアクセス**: 0x00を返す（読み取り）、書き込みは無視

## アーキテクチャ

モジュラー・コンポーネント型設計を採用。各ハードウェアコンポーネントを独立したクラスとして実装し、システムバスを介して通信する。

```
┌──────────────────────────────────────────────────────┐
│                  PC98Emu (メインループ)                 │
│  ┌───────────┐  ┌──────────┐  ┌──────────────┐       │
│  │  CPU       │  │  Bus     │  │  Scheduler   │       │
│  │ (V30/8086) │←→│ (I/O+Mem)│←→│ (タイミング)  │       │
│  └───────────┘  └────┬─────┘  └──────────────┘       │
│                      │                                 │
│  ┌───────┬───────┬───┴───┬────────┬──────────┐        │
│  │ GDC×2 │PIC×2  │ PIT   │  DMA   │  FDC     │        │
│  │uPD7220│i8259A │i8253  │i8237A  │ uPD765A  │        │
│  └───┬───┴───────┴───────┴────────┴────┬─────┘        │
│      │                                  │              │
│  ┌───┴──────────┐  ┌──────────┐  ┌────┴──────┐       │
│  │ VRAM/Display │  │ Keyboard │  │ DiskImage │       │
│  │ (SDL2描画)   │  │ (SDL2入力)│  │ Loader    │       │
│  └──────────────┘  └──────────┘  └───────────┘       │
│                                                        │
│  ┌──────────────┐  ┌─────────┐  ┌─────────────────┐  │
│  │ FM Sound     │  │  RTC    │  │ Compatible BIOS │  │
│  │ YM2608/OPNA  │  │uPD1990A │  │ (独自互換BIOS)   │  │
│  │ (SDL2音声)   │  └─────────┘  └─────────────────┘  │
│  └──────────────┘                                     │
└──────────────────────────────────────────────────────┘
```

### IDevice インターフェース

すべてのデバイスが実装する共通インターフェース:

```csharp
public interface IDevice
{
    byte ReadByte(int port);       // I/Oポート読み取り
    void WriteByte(int port, byte value); // I/Oポート書き込み
    ushort ReadWord(int port);     // ワード読み取り
    void WriteWord(int port, ushort value); // ワード書き込み
    void Reset();                  // デバイスリセット
    void Tick(int cycles);         // クロックサイクル更新
    int[] GetPortRange();          // 管理するポート番号の配列
}
```

### Scheduler（イベント駆動型）

- 優先度付きキューでタイムドイベントを管理
- 各デバイスがイベント（タイマー満了、割り込み発火等）を登録
- CPUサイクルカウンタを基準に、次のイベントまでのサイクル数を計算してCPUを実行
- CPU実行後、期限到達イベントを処理してデバイスの`Tick`を呼び出し
- CPUクロック: 8MHz（V30）を基準

## メモリマップ

PC-98 リアルモード 1MB空間:

| アドレス範囲 | 用途 |
|---|---|
| `0x00000-0x003FF` | IVT（割り込みベクタテーブル、256×4バイト） |
| `0x00400-0x005FF` | BIOSデータエリア（システム構成情報） |
| `0x00600-0x9FFFF` | メインRAM（ユーザー領域） |
| `0xA0000-0xA1FFF` | テキストVRAM（文字コード、2バイト×2000文字） |
| `0xA2000-0xA3FFF` | テキストVRAM（属性、2バイト×2000文字） |
| `0xA8000-0xAFFFF` | グラフィックVRAM（プレーン0: Blue、32KB） |
| `0xB0000-0xB7FFF` | グラフィックVRAM（プレーン1: Red、32KB） |
| `0xB8000-0xBFFFF` | グラフィックVRAM（プレーン2: Green、32KB） |
| `0xE0000-0xE7FFF` | グラフィックVRAM（プレーン3: Intensity、32KB）※バンク切替 |
| `0xE8000-0xFFFFF` | BIOS ROM領域（互換BIOS配置） |

### グラフィックVRAMバンク切替

- I/Oポート`0x00A4`（書き込み）: GDCグラフィックVRAMのプレーン選択
- I/Oポート`0x00A6`（書き込み）: 表示プレーンと書き込みプレーンの切替
- 0xA8000-0xBFFFF の96KB窓に対して、4プレーン（各32KB）をバンク切替でアクセス

### BIOSデータエリア（0x0400-0x05FF）

互換BIOS初期化時に以下を設定:
- `0x0400`: キーボードバッファ
- `0x0458`: メモリサイズ（KB単位）
- `0x045B`: ブートデバイス
- `0x0480`: 表示モード
- `0x0486`: GDCクロックモード（2.5MHz/5MHz）
- その他システム構成フラグ

## I/Oポート割り当て

I/Oポートはバイト単位で正確にルーティングする（範囲の重複はバイトレベルで解決）:

| ポート | デバイス |
|---|---|
| `0x00`, `0x02` | PIC1 マスター (i8259A) |
| `0x01`, `0x03`, `0x05`, `0x07`, `0x09`, `0x0B`, `0x0D`, `0x0F` | DMA (i8237A) |
| `0x08`, `0x0A` | PIC2 スレーブ (i8259A) |
| `0x20` | RTC (uPD1990A) |
| `0x30-0x33` | シリアル (i8251) — スタブ実装 |
| `0x35`, `0x37` | システムポート（Beep制御含む） |
| `0x40`, `0x42` | プリンタ（スタブ — ビジー応答） |
| `0x41`, `0x43` | キーボード |
| `0x60`, `0x62`, `0x64`, `0x66`, `0x68` | GDCテキスト (uPD7220) |
| `0x71`, `0x73`, `0x75`, `0x77` | PIT (i8253) |
| `0x90`, `0x92`, `0x94` | FDC (uPD765A) |
| `0xA0`, `0xA2`, `0xA4`, `0xA6`, `0xA8` | GDCグラフィック (uPD7220) + VRAM制御 |
| `0x0188`, `0x018A`, `0x018C`, `0x018E` | FM音源 (YM2608) |
| `0x0CC0-0x0CCC` | SASI HDDコントローラ — スタブ実装 |
| `0x7FD9-0x7FDD` | バスマウス |

## CPU エミュレーション（V30/i8086）

### レジスタセット
- 汎用: AX(AH/AL), BX(BH/BL), CX(CH/CL), DX(DH/DL)
- インデックス: SI, DI, BP, SP
- セグメント: CS, DS, ES, SS
- IP（命令ポインタ）、FLAGS

### CPUクロック
- 8MHz（V30標準）
- Schedulerがこのクロックを基準にデバイスタイミングを計算

### 命令デコーダ
- 1バイト目のオペコードでメインテーブル（256エントリ）を引く
- `0x0F`プレフィックスで拡張テーブル（V30拡張命令）
- ModR/Mバイト解析でオペランド（レジスタ/メモリ）を決定
- プレフィックス処理: セグメントオーバーライド、REP、LOCK

### 命令カテゴリ
- 算術: ADD, SUB, MUL, DIV, INC, DEC, NEG, CMP, ADC, SBB
- 論理: AND, OR, XOR, NOT, TEST
- シフト: SHL, SHR, SAR, ROL, ROR, RCL, RCR
- 転送: MOV, PUSH, POP, XCHG, LEA, LDS, LES
- 文字列: MOVSB/W, CMPSB/W, STOSB/W, LODSB/W, SCASB/W (+REP)
- 分岐: JMP, CALL, RET, Jcc (全条件), LOOP, INT, IRET
- I/O: IN, OUT
- V30拡張: BRKEM, REPC, REPNC, INS, EXT 等

### 割り込み処理
- IVT（割り込みベクタテーブル）は`0x00000-0x003FF`（256エントリ×4バイト）
- ハードウェア割り込み: PICからIRQ→CPU INT
- ソフトウェア割り込み: `INT nn`命令 → 互換BIOSハンドラにディスパッチ

### サイクルカウント
- 命令ごとにクロックサイクル数を加算
- Schedulerがデバイス更新タイミングを判断するために使用

## 画面表示（GDC + VRAM + SDL2描画）

### GDC (uPD7220) × 2基
- **テキストGDC**: テキストVRAMを管理。文字コード（JIS/シフトJIS）＋属性（色、点滅、反転、アンダーライン）
- **グラフィックGDC**: グラフィックVRAM（4プレーン構成）を管理。640×400 / 640×200対応

### 初期実装するGDCコマンド
- RESET, SYNC, CSRFORM（カーソル形状）, CSRW（カーソル位置設定）
- START, STOP, SCROLL, PRAM（パラメータRAM設定）
- WRITE, READ, DMAW, DMAR（VRAM読み書き）
- PITCH（表示ピッチ設定）

### 将来対応（必要に応じて追加）
- ZOOM, CCHAR, FIGS+GCHRD/RDAT/WDAT（図形描画）

### テキスト表示
- テキストVRAM（0xA0000-0xA1FFF）から文字コード（2バイト）を読み取り
- テキストVRAM（0xA2000-0xA3FFF）から属性（2バイト）を読み取り
- 内蔵フォントデータ（ANK: 8×16ドット、漢字: 16×16ドット）
- ANK文字（半角）＋JIS第一・第二水準漢字（全角）

### フォントデータ
- ANK文字（256文字）: コード内に定数配列として定義（パブリックドメインのビットマップフォント）
- JIS漢字: オープンソースの美咲フォント（misaki_gothic）等のビットマップデータを変換して組み込み、またはFreeTypeで生成
- フォントデータは`Font.cs`内にバイト配列として格納

### グラフィック表示
- 4プレーン（B/R/G/I）の各ビットを合成して16色パレットに変換
- デジタル8色 / アナログ16色パレットモード対応

### SDL2描画パイプライン
- テキストレイヤーとグラフィックレイヤーを合成
- 各フレームでVRAMからRGBAバッファに変換
- `SDL_UpdateTexture`でGPUにアップロード
- 56.4Hz（PC-98実機リフレッシュレート）でVSync

### 解像度
- 640×400（標準）、640×200（一部ゲーム）
- ウィンドウスケーリング対応（×1, ×2, ×3）

## サウンド（YM2608/OPNA）

### YM2608エミュレーション
- FM音源6チャンネル（4オペレータ×6ch）
- SSG音源3チャンネル（AY-3-8910互換）
- リズム音源（6種：バスドラム、スネア、シンバル、ハイハット、タム、リムショット）
- ADPCM 1チャンネル
- Timer A / Timer B（割り込み生成、多くのゲームの音楽再生で必須）
- SSG-EG（FM チャンネルのSSGタイプエンベロープ）

### FM合成
- 各オペレータ: 位相ジェネレータ + サイン波テーブル + エンベロープジェネレータ(ADSR)
- 8つのアルゴリズム（オペレータ接続パターン）
- LFO（振幅変調 / 周波数変調）

### SSG合成
- 矩形波3ch + ノイズジェネレータ
- エンベロープパターン（周期的な音量変化）

### SDL2音声出力
- `SDL_AudioSpec`: 44100Hz / 16bit / ステレオ
- コールバック方式でバッファに書き込み
- CPUサイクルと同期してサンプル生成（YM2608内部クロック: 7.987MHz）

### Beep音
- システムポート`0x37`でBeep ON/OFF制御
- PIT Ch2周波数でBeep音程を決定
- サウンドミキサーでFM/SSG/Beepを合成

### 実装アプローチ
- FM合成はルックアップテーブル（サイン波、エンベロープ）で高速化

## ディスクI/O

### FDC (uPD765A) エミュレーション
- コマンド: READ DATA, WRITE DATA, READ ID, SEEK, RECALIBRATE, SENSE INT STATUS等
- DMA転送（i8237A経由）でメモリにセクタデータを転送
- ステータスレジスタ（MSR、ST0-ST3）の状態管理

### SASI HDDコントローラ（スタブ実装）
- I/Oポート`0x0CC0-0x0CCC`
- HDI/NHDイメージに対してCHS方式でセクタ読み書き
- 基本コマンド: TEST UNIT READY, READ, WRITE, REQUEST SENSE

### ディスクイメージフォーマット対応

| フォーマット | 種別 | 対応内容 |
|---|---|---|
| D88 | FDD | ヘッダ解析→トラック/セクタ構造読み取り |
| FDI | FDD | ヘッダ＋生セクタデータ |
| NFD (r0/r1) | FDD | 基本対応（コピープロテクト再現はストレッチゴール） |
| HDI | HDD | ヘッダ＋CHS/LBAアクセス |
| NHD | HDD | New HDD Image フォーマット |

## 互換BIOS

BIOSはINT命令ハンドラとして実装し、実機のBIOS互換APIを提供:

| INT | 機能 |
|---|---|
| `INT 18h` | ディスクBIOS（セクタ読み書き、ドライブ情報） |
| `INT 19h` | RS-232C BIOS（シリアル通信） |
| `INT 1Ah` | タイマーBIOS（時刻取得 — RTC経由） |
| `INT 1Bh` | キーボードBIOS（キー入力、バッファ管理） |
| `INT 1Ch` | CRT BIOS（画面制御、カーソル） |
| `INT 1Dh` | グラフィックBIOS（直線、円、塗りつぶし） |
| `INT 1Eh` | プリンタBIOS（スタブ — 常にビジー応答） |
| `INT 1Fh` | シリアルBIOS（スタブ） |

### ブートシーケンス
1. 互換BIOSが初期化処理（BIOSデータエリア設定、デバイス初期化、IVT設定）
2. IPL（Initial Program Loader）: FDD/HDDの先頭セクタ（C:0/H:0/S:1）を0x1FE0:0000に読み込み
3. 読み込んだIPLセクタにジャンプして実行（PC-98はブートシグネチャチェックなし）

## 入力デバイス

### キーボード（I/Oポート 0x41-0x43）
- SDL2キーイベント → PC-98スキャンコード変換テーブル
- キーバッファ（16バイトリングバッファ）
- IRQ1でCPUに割り込み通知
- 特殊キー: GRPH, NFER, XFER, vf1-vf5, STOP, COPY等のマッピング

### マウス（バスマウス）
- SDL2マウスイベント → 相対移動量変換
- I/Oポート 0x7FD9-0x7FDD
- IRQ13で割り込み

## その他デバイス

### PIC (i8259A) × 2基（マスター/スレーブ）
- IRQ0: PIT（タイマー）、IRQ1: キーボード、IRQ3: COM2、IRQ4: COM1
- IRQ5: 予備、IRQ6: FDD、IRQ8-15: スレーブPIC（FM音源、SCSI等）
- ICW/OCW コマンド処理、ISR/IRR/IMRレジスタ管理

### PIT (i8253) 3チャンネル
- Ch0: システムタイマー（IRQ0、10ms間隔）
- Ch1: DRAM リフレッシュ（エミュレーション対象外、スタブ）
- Ch2: Beep音周波数生成
- モード0-5対応

### DMA (i8237A)
- FDCのデータ転送に使用
- チャンネル2: FDD
- アドレス/カウンタレジスタ管理

### RTC (uPD1990A)
- I/Oポート`0x20`
- 年月日時分秒の取得（ホストPCの時刻を返す）
- INT 1Ah（タイマーBIOS）から使用

### シリアル (i8251) — スタブ
- I/Oポート`0x30-0x33`
- ステータス読み取り時「送信可能・受信なし」を返す

## プロジェクト構成

```
PC98Emu/
├── PC98Emu.sln
├── PC98Emu/
│   ├── Program.cs              # エントリポイント
│   ├── Emulator.cs             # メインループ・統合管理
│   ├── CPU/
│   │   ├── V30.cs              # CPU本体（レジスタ、命令実行）
│   │   ├── Instructions.cs     # 命令デコーダ・実行
│   │   ├── ModRM.cs            # ModR/M解析
│   │   └── Flags.cs            # FLAGSレジスタ管理
│   ├── Bus/
│   │   ├── SystemBus.cs        # メモリ・I/Oバス統合
│   │   ├── IDevice.cs          # デバイスインターフェース
│   │   └── MemoryMap.cs        # メモリ領域マッピング
│   ├── Graphics/
│   │   ├── GDC.cs              # uPD7220エミュレーション
│   │   ├── TextRenderer.cs     # テキストVRAM描画
│   │   ├── GraphicsRenderer.cs # グラフィックVRAM描画
│   │   ├── Display.cs          # SDL2画面出力
│   │   └── Font.cs             # 内蔵フォントデータ
│   ├── Sound/
│   │   ├── YM2608.cs           # FM音源エミュレーション
│   │   ├── FMChannel.cs        # FMチャンネル・オペレータ
│   │   ├── SSG.cs              # SSG音源
│   │   ├── ADPCM.cs            # ADPCMデコーダ
│   │   └── AudioOutput.cs      # SDL2音声出力
│   ├── Devices/
│   │   ├── PIC.cs              # i8259A
│   │   ├── PIT.cs              # i8253
│   │   ├── DMA.cs              # i8237A
│   │   ├── FDC.cs              # uPD765A
│   │   ├── RTC.cs              # uPD1990A
│   │   ├── Serial.cs           # i8251（スタブ）
│   │   ├── Keyboard.cs         # キーボードコントローラ
│   │   └── Mouse.cs            # バスマウス
│   ├── Disk/
│   │   ├── DiskManager.cs      # ディスク管理
│   │   ├── D88Image.cs         # D88フォーマット
│   │   ├── FDIImage.cs         # FDIフォーマット
│   │   ├── NFDImage.cs         # NFDフォーマット
│   │   ├── HDIImage.cs         # HDIフォーマット
│   │   ├── NHDImage.cs         # NHDフォーマット
│   │   ├── SASIController.cs   # SASI HDDコントローラ（スタブ）
│   │   └── IDiskImage.cs       # ディスクイメージIF
│   ├── BIOS/
│   │   ├── CompatibleBios.cs   # 互換BIOS統合
│   │   ├── DiskBios.cs         # INT 18h
│   │   ├── SerialBios.cs       # INT 19h（スタブ）
│   │   ├── TimerBios.cs        # INT 1Ah
│   │   ├── KeyboardBios.cs     # INT 1Bh
│   │   ├── CrtBios.cs          # INT 1Ch
│   │   ├── GraphicsBios.cs     # INT 1Dh
│   │   └── BootLoader.cs       # ブートシーケンス
│   └── Scheduler/
│       └── Scheduler.cs        # イベント駆動型タイミング同期
├── PC98Emu.Tests/
│   └── ...                     # ユニットテスト
└── README.md
```

## 依存パッケージ
- `SDL2-CS`（NuGet: `ppy.SDL2-CS` — 活発にメンテナンスされているフォーク）— 描画・音声・入力
- `xunit` — テストフレームワーク

## ターゲット
- .NET 8.0 (LTS)
- Windows対応（SDL2によりLinux/macOSも将来対応可能）

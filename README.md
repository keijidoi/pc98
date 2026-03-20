# PC98Emu - NEC PC-9801/PC-9821 Emulator

C# (.NET 8) で実装された NEC PC-9801/PC-9821 エミュレータです。

## 環境構築

### 必要要件

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Visual Studio Code](https://code.visualstudio.com/)
- SDL2 ランタイムライブラリ (音声・画面出力に必要)
  - Windows: NuGet パッケージ (`ppy.SDL2-CS`) により自動解決されます
  - Linux/macOS: `libSDL2` をパッケージマネージャでインストールしてください

### VS Code 推奨拡張機能

- [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) - C# 開発サポート (IntelliSense, デバッグ, テスト)
- [C#](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp) - C# 言語サポート (OmniSharp)

### VS Code でのセットアップ

1. リポジトリをクローン:
   ```bash
   git clone https://github.com/keijidoi/pc98.git
   cd pc98
   ```

2. VS Code でフォルダを開く:
   ```bash
   code .
   ```

3. 拡張機能のインストールを促すダイアログが表示されたら、推奨拡張機能をインストールしてください

4. ビルド:
   ```bash
   cd PC98Emu/PC98Emu
   dotnet build
   ```

5. テスト:
   ```bash
   cd PC98Emu/PC98Emu.Tests
   dotnet test
   ```

### VS Code でのデバッグ

VS Code のターミナルから直接実行できます:

```bash
cd PC98Emu/PC98Emu
dotnet run -- game.hdi
```

C# Dev Kit を使用している場合は、`launch.json` に引数を設定してデバッガ付きで実行することも可能です。

## 使い方

### 基本構文

```bash
dotnet run -- [オプション] <ディスクイメージ...>
```

### 対応フォーマット

| 形式 | 拡張子 | 種別 |
|------|--------|------|
| D88/D98 | `.d88`, `.d98`, `.88d`, `.98d` | フロッピーディスク |
| HDM/TFD/XDF/DUP | `.hdm`, `.tfd`, `.xdf`, `.dup` | フロッピーディスク (RAW) |
| FDI | `.fdi` | フロッピーディスク |
| HDI | `.hdi` | ハードディスク |
| NHD | `.nhd` | ハードディスク |
| NFD | `.nfd` | ハードディスク |

### オプション

| オプション | 説明 |
|-----------|------|
| `--boot <drive>` | ブートドライブ指定 (`fd0`~`fd3`, `hd0`, `hd1`)。省略時は自動検出 |
| `--fd<N> <file>` | 指定ドライブ番号にフロッピーイメージをロード (例: `--fd2 disk.hdm`) |
| `--hd<N> <file>` | 指定ドライブ番号に HDD イメージをロード |
| `--mount <path>` | ホストディレクトリを DOS ドライブとしてマウント |

### 使用例

HDD イメージから MS-DOS を起動:

```bash
dotnet run -- game.hdi
```

HDD + フロッピーディスクの組み合わせ:

```bash
dotnet run -- --fd2 boot.hdm game.hdi
```

ホストディレクトリマウントを使用:

```bash
dotnet run -- --fd2 boot.hdm --mount /path/to/game_files
```

## アーキテクチャ

```
PC98Emu/
  CPU/          - NEC V30 (8086互換) CPU エミュレーション
  Bus/          - システムバス、I/O ポート管理
  BIOS/         - PC-98 BIOS 互換レイヤー (INT 18h/1Bh/21h 等)
  Disk/         - ディスクイメージ読み込み、FAT12/16 リーダー、ホストディレクトリマウント
  Graphics/     - GDC (uPD7220)、テキスト/グラフィックスレンダラー、漢字ROM、GRCG
  Sound/        - YM2608 (OPNA) サウンドチップ (FM/SSG/ADPCM)
  Devices/      - PIC, PIT, DMA, FDC, キーボード, マウス, カレンダIC 等
  Scheduler/    - タイミング制御
  Emulator.cs   - メインエミュレーションループ
  Program.cs    - CLI エントリーポイント
```

## 実装状況

### 動作確認済み

- MS-DOS 6.20 の起動 (HDD イメージ経由)
- DOS コマンドライン入力・実行 (dir コマンド等)
- 日本語テキスト表示 (Shift-JIS)
- ファンクションキー (F1-F10) サポート
- テキスト VRAM レンダリング
- 基本的な DOS ファイル操作 (INT 21h)

### 未完成・制限事項

- **グラフィックス VRAM 描画**: GRCG (Graphic Charger) の実装は基本部分のみ。EGC (Enhanced Graphic Charger) は未実装
- **NWWCG グラフィックスライブラリ**: INT DCh ハンドラの自動インストールが動作しない。NWWCG.EXE を使用するゲームのグラフィックス表示に影響
- **合成ブート (Synthetic Boot)**: HDD なし + ホストディレクトリマウントのみでの起動は実験的。一部のゲームで画面が表示されない問題あり
- **サウンド出力**: YM2608 の基本実装はあるが、全レジスタの完全エミュレーションではない。ADPCM の再生精度に制限あり
- **CPU 命令**: V30 固有命令の一部が未実装の可能性あり
- **割り込みタイミング**: PIT/PIC のタイミング精度は簡易的な実装
- **マウス**: 基本的な座標取得のみ。一部のゲームで正しく動作しない可能性あり
- **シリアルポート / プリンタ**: スタブ実装のみ
- **SASI コントローラ**: 基本的な HDD アクセスのみ

## ライセンス

本プロジェクトは個人の学習・研究目的で開発されています。

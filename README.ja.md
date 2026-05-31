# Everyday Slideshow

[English](README.md) | **日本語**

Everyday Slideshow は、Windows 11 向けのローカル完結型スライドショー閲覧アプリです。クラウドアルバム、広告付きアプリ、重い写真管理ソフトではありません。手元のフォルダを登録するだけで、写真や動画を気持ちよく流し、見ながら軽く整理できます。

## 特徴

- 複数フォルダ登録、表示名、サブフォルダ読み込み、安全モードで避ける個人用フォルダ指定。
- 全部混ぜる、単一フォルダ、お気に入り、あとで見る、最近見ていない写真、任意フォルダを組み合わせたプレイリスト再生。
- スマートシャッフルは一巡前の重複を避け、最近見た写真の再登場を抑え、フォルダ、日付、名前、お気に入りを自然に混ぜます。
- 順番再生、ループ、一時停止/再開、前へ/次へ、最初へ、前回位置から再開。
- 速度は 1 秒、2 秒、3 秒、5 秒、10 秒、30 秒、1 分、5 分、カスタム秒数。
- 写真と動画の混在再生。Windows が再生できる動画は終了後に次へ進みます。
- 気分モード: デフォルト、仕事中、寝る前、安全モード、ユーザー作成のカスタムモード。
- ホーム画面は枠付きウィンドウで表示し、再生中のみフルスクリーン/枠なし、モニター選択、常に最前面、ダークモード、明るさ、控えめ表示、ウィンドウサイズ復元。
- 再生中はメニューバーを隠し、マウス移動時だけ操作 UI を表示。
- お気に入り、非表示/解除、削除候補/解除、あとで見る/解除。
- 削除候補の実削除は、確認後に Windows のごみ箱へ移動。
- SQLite にフォルダ、メタデータ、タグ、履歴、シャッフル状態、設定を保存。
- バックグラウンドスキャン、サムネイル/メタデータキャッシュ、読み込めないファイルのスキップ。
- Windows 起動時自動起動、この画面のアイドル時の自動再生、スリープ復帰後の再開。
- 英語 UI がデフォルトで、Language/言語 メニューから日本語へ切り替え可能。

## インストール

GitHub Releases からダウンロードしてください。

- ポータブル EXE: `EverydaySlideshow-<version>-portable-win-x64.exe`
- ポータブル ZIP: `EverydaySlideshow-<version>-portable-win-x64.zip`
- インストーラー EXE: `EverydaySlideshow-<version>-setup-win-x64.exe`
- インストーラー ZIP: `EverydaySlideshow-<version>-setup-win-x64.zip`

ポータブル版は好きな場所に置いて使えます。インストーラー版は Program Files にインストールし、通常の Windows ショートカットを作成します。

## 使い方

1. アプリを起動します。
2. **Add folder / フォルダを追加** から写真や動画のフォルダを選びます。
3. 必要に応じて気分モードを選びます。
4. **Shuffle all / 全部混ぜる**、登録フォルダ、お気に入り、あとで見る、最近見ていない写真、プレイリストから再生します。
5. 再生中はマウスを動かすと操作 UI が表示されます。
6. ホーム画面のメニューバーから、フォルダ、プレイリスト、気分モード、表示設定、言語、更新確認、各種設定にアクセスできます。

## ショートカット

- `Space`: 一時停止/再開
- `Left` / `Right`: 前へ / 次へ
- `Home`: 最初へ
- `F11` または `F`: フルスクリーン切り替え
- `Esc`: フルスクリーン/枠なし解除。通常表示ではホームへ戻る
- `S` または `+`: お気に入り
- `H`: 非表示/解除
- `Delete`: 削除候補/解除

## 対応形式

画像:

- 必須対応: `jpg`, `jpeg`, `png`, `bmp`, `gif`, `tiff`, `webp`
- 可能な限り対応: `heic`, `heif`, `avif`
- RAW プレビュー/サムネイル対応: `dng`, `cr2`, `cr3`, `nef`, `arw`, `orf`, `rw2`, `raf`, `pef`, `srw`

動画:

- `mp4`, `m4v`, `mov`, `webm`, `wmv`, `avi`, `mkv`

実際のデコード可否は Windows の WIC/Media Foundation コーデックに依存します。HEIC、AVIF、RAW、一部動画は Microsoft Store 拡張機能やメーカー製コーデックが必要な場合があります。

## 更新

アプリは自動でインターネットへ接続して更新確認しません。必要なときだけ **Help > Check for updates... / ヘルプ > 更新を確認...** を選んでください。

そのメニューを選んだ時だけ、このリポジトリの GitHub Releases latest API に接続します。新しいリリースがある場合は、既定のブラウザで GitHub Releases ページを開きます。アプリが自動で更新をダウンロード/インストールすることはありません。

## プライバシー

- アカウント不要。
- 広告、トラッキング、クラウド同期、テレメトリなし。
- 写真、動画、メタデータ、フォルダ、タグを外部送信しません。
- ネットワーク接続は、ユーザーが **更新を確認** を選んだ時だけ使います。

ローカル保存場所:

- DB: `%LOCALAPPDATA%\EverydaySlideshow\slideshow.db`
- キャッシュ: `%LOCALAPPDATA%\EverydaySlideshow\Cache`
- サムネイル: `%LOCALAPPDATA%\EverydaySlideshow\Cache\Thumbnails`

リセット:

1. アプリを終了します。
2. `%LOCALAPPDATA%\EverydaySlideshow` を削除します。
3. Windows 起動時自動起動を有効にしていた場合は、先にアプリで無効化するか、`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` の `EverydaySlideshow` を削除します。

## ライセンス

このプロジェクトは Creative Commons Attribution-NonCommercial 4.0 International License (`CC BY-NC 4.0`) で公開されます。

商用利用は、別途許可がない限り認められません。詳細は [LICENSE](LICENSE) を確認してください。

## 既知制限

- 動画再生は WPF の `MediaElement` を使うため、対応形式は Windows 側のコーデックに依存します。
- HEIC/AVIF/RAW は Windows コーデックがある場合のみ表示できます。RAW はフル現像ではなくプレビュー/サムネイル相当です。
- アニメーション GIF は環境によって先頭フレーム表示になる場合があります。

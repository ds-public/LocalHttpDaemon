# LHTTPD プロジェクト指針

## プロジェクトの目的

このプロジェクトでは、アプリケーションの実行中だけ HTTP サーバーとして動作し、HTML などのコンテンツを HTTP/1.1 で返す簡易ウェブサーバーを作成します。サーバーはポート 80 または 8080 で待ち受けます。停止すると待ち受けも終了する設計とします。

## アプリケーション構成

コンソール版と GUI 版を、共通の Visual Studio 2026 用ソリューション `LocalHttpDaemon.slnx` で開発します。実行ファイルとプロジェクトは独立させます。

- `LocalHttpDaemon.slnx` — 両版と動作検証用プロジェクトを含むソリューション
- `CLI_LocalHttpDaemon/` — コンソール版のプロジェクト
- `CLI_LocalHttpDaemon_Verification/` / `GUI_LocalHttpDaemon_Verification/` — 各版の動作検証用プロジェクト
- `GUI_LocalHttpDaemon/` — GUI 版のプロジェクト

現時点のプロジェクト設定では、コンソール版は .NET 10 (`net10.0`)、GUI 版は WPF を使用する Windows 向け .NET 10 (`net10.0-windows`) です。

各版の Release ビルドでは、`CLI_LocalHttpDaemon_Package/` と `GUI_LocalHttpDaemon_Package/` にそれぞれ自己完結型 EXE と ZIP を生成します。配置の詳細は `Documents/ソリューション構成・ビルド仕様.md` を参照します。

## 開発時の指針

- インデントにはタブ（`\t`）を使用します。スペースによるインデントは使用しません。
- 改行コードは LF を使用します。CRLF は使用しません。

- C# コードの新規作成・変更時は、`Documents/CodingFormat.md` のコーディング書式ルールに従います。
- コンソール版の機能や UI は `CLI_LocalHttpDaemon` 側に、GUI 版の機能や UI は `GUI_LocalHttpDaemon` 側に実装します。
- どちらの版も、上記の目的（アプリ実行中のみ待ち受け、ポート 80 または 8080、HTTP/1.1 によるコンテンツ応答）に沿って変更します。
- ソリューションとプロジェクトの既存の配置・対応関係を保ちます。片方の版に関する変更を、理由なくもう片方へ適用しないでください。
- 新たな仕様が必要な場合は、既存の実装や設定から決めつけず、依頼内容に基づいて判断します。

## 仕様書の管理

- ユーザーの指示に仕様（アプリケーションの要件・動作・制約など）が含まれる場合、その内容を `Documents/` 内の適切な名前の仕様書に記録します。
- 既存仕様の変更や追加は、関連する仕様書を更新して記録します。仕様書間で内容が食い違わないようにします。
- `Documents/` は LocalHttpDaemon 直下に配置し、プロジェクトの仕様・設計文書を保存する場所です。

# CLI版 クラス・メソッドリファレンス

実装確認日: 2026-10-27

## 対象・読み方

CLI_LocalHttpDaemon（.NET 10）の実装リファレンス。クラス概要、全明示フィールド・プロパティ、コンストラクター、public/privateメソッド、名前付きローカル関数を説明する。現状のコードの説明であり、新しい動作仕様を追加するものではない。検証用プロジェクト、.NETの継承メンバー、コンパイラー生成メンバーは対象外。

すべてLHTTPD名前空間に属する。Taskは処理完了、Task<T>は完了時の値を表す。型の末尾の「?」はnull許容。ソースリンクは本書からの相対パス。

## 目次

- [クラス一覧と処理の流れ](#クラス一覧と処理の流れ)
- [Program](#program-クラス)
- [ServerSettings](#serversettings-クラス)

## クラス一覧と処理の流れ

| クラス | 役割 | ソース |
| --- | --- | --- |
| Program | CLI起動、引数解析、待ち受け、配信、ログ、証明書管理 | [Program.cs](../CLI_LocalHttpDaemon/Program.cs) |
| ServerSettings | 設定の保持・読み込み・保存 | [ServerSettings.cs](../GUI_LocalHttpDaemon/ServerSettings.cs) |

Main → 設定読み込み・引数上書き・検証 → 証明書準備 → HTTP/HTTPS開始 → 必要時に設定保存 → 2本のAcceptLoopAsync → 接続ごとのHandleClientAsync → 応答・ログ。

Ctrl+Cでキャンセルし、両リスナーを停止する。GUI版と異なり、個々の接続タスクを一覧で保持して終了待機する実装ではない。CLIプロジェクトはServerSettings.csをGUIフォルダからリンクし、GUIアセンブリには依存しない。

## Program クラス

宣言: `public class Program`

定義: [Program.cs](../CLI_LocalHttpDaemon/Program.cs)

概要: エントリーポイントとHTTP/HTTPSサーバー機能をまとめる。明示メソッドはすべてstatic。IPv4 loopbackのみで待ち受ける。

### フィールド・プロパティ

| フィールド | 型・修飾子 | 値・用途 |
| --- | --- | --- |
| DefaultHttpPort | private const int | 80。保存済み設定がない／読み込み失敗時のHTTP既定値。 |
| DefaultHttpsPort | private const int | 443。HTTPSの既定値。 |
| MaximumHeaderBytes | private const int | 32768。受信ヘッダー蓄積の上限判定値。 |
| m_logLock | private static readonly object | 複数接続からのログ文字列出力・色変更を排他する。 |

明示プロパティはない。

### Program()

- 種類: 暗黙のpublicインスタンスコンストラクター。
- 機能: インスタンス生成。アプリ起動処理は行わない。
- 引数: なし。
- 戻り値: コンストラクターのためなし。

### Main( args )

宣言: `public static async Task<int> Main( string[] args )`

- 種類: public・static・async・エントリーポイント。
- 機能: 出力をBOMなしUTF-8に設定し、ヘルプ、設定読み込み、引数解析、ルート検証、証明書準備、HTTP/HTTPS起動、設定保存、終了待機を行う。
- 引数: argsはコマンドライン引数配列。--root <絶対パス>、--port <HTTP>[/<HTTPS>]、--help / -h を解釈する。
- 戻り値: Task<int>。下表の終了コードで完了する。

| 値 | 意味 |
| --- | --- |
| 0 | 通常停止、ヘルプ表示、またはルート未指定による案内表示。 |
| 1 | 証明書準備、ポート開始、設定保存の失敗。 |
| 2 | 引数不正、ルートパス不正・不存在、ポート指定不正。 |

ヘルプ指定が配列内にあれば先に処理し、設定は読み書きしない。設定の読み込み失敗は標準エラーへ通知して既定値へ戻す。引数は保存設定を上書きする。HTTPSポート省略時は既存値を維持する。両ポート開始後、設定未作成または値が変わったときだけ保存する。保存失敗では両リスナーを停止する。

Ctrl+Cの無名イベントハンドラーは既定の即時終了を抑止し、停止トークンをキャンセルする。Task.WhenAllでHTTP/HTTPSの受付ループを待機する。受付中の想定外例外すべてを終了コードへ変換する実装ではない。

### PrintUsage()

宣言: `private static void PrintUsage()`

- 種類: private・static・同期メソッド。
- 機能: オプション、指定形式、保存設定の上書き方法を標準出力へ表示する。
- 引数: なし。
- 戻り値: void（なし）。

### NormalizePathSeparators( path )

宣言: `private static string NormalizePathSeparators( string path )`

- 種類: private・static・同期メソッド。
- 機能: / と \ を実行環境のディレクトリ区切りへ置換する。
- 引数: pathは入力パス文字列。
- 戻り値: 区切りを統一した文字列。絶対化・存在確認は行わない。

### AcceptLoopAsync( listener, useTls, certificate, root, cancellationToken )

宣言: `private static async Task AcceptLoopAsync( TcpListener listener, bool useTls, X509Certificate2 certificate, string root, CancellationToken cancellationToken )`

- 種類: private・static・asyncメソッド。
- 機能: キャンセルまでTCP接続を受け付け、各接続のHandleClientAsyncを開始する。接続処理を待たず次の受付へ進む。
- 戻り値: Task。受付ループの終了。全接続処理の終了を意味しない。

| 引数 | 型 | 意味 |
| --- | --- | --- |
| listener | TcpListener | 開始済みのHTTPまたはHTTPSリスナー。 |
| useTls | bool | 受けた接続にTLSを使用するか。 |
| certificate | X509Certificate2 | 接続処理へ渡すサーバー証明書。 |
| root | string | 接続処理へ渡す配信ルート。 |
| cancellationToken | CancellationToken | アプリ終了による受付・接続処理の停止通知。 |

キャンセル例外、およびキャンセル後のSocketExceptionでループを終了する。それ以外の受付エラーは呼び出し元へ伝播する。

### WriteAccessLog( method, path, timestamp, statusCode, error )

宣言: `private static void WriteAccessLog( string method, string path, DateTime timestamp, int statusCode, string? error )`

- 種類: private・static・同期メソッド。
- 機能: アクセス結果を1行に整形し、色付きでコンソールへ出力する。m_logLockで排他し、出力後は元の文字色へ戻す。
- 戻り値: void（なし）。

| 引数 | 型 | 意味 |
| --- | --- | --- |
| method | string | 要求メソッド。要求解析前のエラーではERROR。 |
| path | string | クエリ・フラグメントを除いた要求パス。先頭/を省略し、空なら/を表示。 |
| timestamp | DateTime | 接続処理開始時のローカル時刻。yyyy/MM/dd HH:mmで表示。 |
| statusCode | int | 記録済みのHTTP応答コード。0ならERRORと表示。 |
| error | string? | エラー詳細。nullなら付加しない。 |

表示は「[メソッド] 時刻 結果 パス」、エラーがあれば「 — 詳細」を付ける。色の優先順位は、エラーあり・404・500以上が赤、次にGET以外・300～399が黄、残る400以上が赤、それ以外が白。

#### ローカル関数 SingleLine

宣言: `static string SingleLine( string value )`

- 種類: WriteAccessLog内のstatic・同期ローカル関数。
- 機能: 制御文字を空白へ置換してログの複数行化を防ぐ。
- 引数: valueは整形対象文字列。
- 戻り値: 制御文字を置換した新しい文字列。

### HandleClientAsync( client, useTls, certificate, root, cancellationToken )

宣言: `private static async Task HandleClientAsync( TcpClient client, bool useTls, X509Certificate2 certificate, string root, CancellationToken cancellationToken )`

- 種類: private・static・asyncメソッド。
- 機能: TCP接続を所有し、必要ならTLS認証を行い、HTTP要求を読み取ってファイルまたはエラー応答を返す。最後に接続を破棄し、アクセス結果を記録する。
- 戻り値: Task。1接続分の処理完了。HTTPステータスを返すものではない。

| 引数 | 型 | 意味 |
| --- | --- | --- |
| client | TcpClient | 受け付けた接続。メソッド終了時に破棄する。 |
| useTls | bool | trueならTLS 1.2/1.3によるサーバー認証を行う。 |
| certificate | X509Certificate2 | TLS用サーバー証明書。HTTPでは認証に使用しない。 |
| root | string | ファイル配信元のルートパス。 |
| cancellationToken | CancellationToken | サーバー停止通知。30秒の接続タイムアウトと連結する。 |

処理の詳細:

1. 接続処理開始時のローカル時刻を保持し、要求メソッド・パス・応答ステータス・エラーをログ用に管理する。
2. TLSの場合、クライアント証明書は要求せず、証明書失効確認は行わない。
3. 2048バイト単位で受信してヘッダー終端を探す。蓄積が32768バイト以上なら431。要求行の要素不足、またはバージョンがHTTP/1.0・HTTP/1.1以外なら400。
4. 要求パスからクエリとフラグメントを除く。GET/HEAD以外には Allow: GET, HEAD 付き405を返す。
5. URLデコードしたパスをルートと結合して絶対化する。空パスとディレクトリ要求は index.html に対応させる。大文字小文字を無視した文字列比較でルート外と判定したパスは403。
6. CLI版では、GUI版にあるルート配下の各要素の再解析ポイント・コロン検査は行わない。
7. ファイルがなければ404。ファイル全体をバイト列で読み込み、200と拡張子対応のContent-Typeを返す。HEADでもファイルを読み込むが本文は送信しない。
8. タイムアウト、通信・ファイル処理、TLS認証、その他の処理例外をログ用エラーへ変換する。例外時に500を自動送信する処理はない。

通常停止によるキャンセルではログを抑制する。応答ステータスまたはエラーがあるときだけ記録し、要求を送らず正常切断した接続は記録しない。ステータスは送信完了前に設定するため、送信失敗時もステータスとエラーを併記することがある。ログ通知・出力自体で発生した例外は、この処理のcatchの対象外。

#### ローカル関数 RespondAsync

宣言: `Task RespondAsync( Stream stream, int statusCode, string reason, string body, string contentType, bool headOnly, CancellationToken token, string extraHeaders = "" )`

- 種類: HandleClientAsync 内の非staticローカル関数。自身にはasync修飾子がない。
- 機能: ログ用応答ステータスを更新し、WriteResponseAsync に委譲する。
- 引数: streamは送信先、statusCodeはHTTPステータス、reasonは理由句、bodyはUTF-8化する本文、contentTypeはMIMEタイプ、headOnlyは本文省略指定、tokenは送信キャンセル通知、extraHeadersは追加ヘッダー（既定は空文字列）。
- 戻り値: Task。委譲した送信処理の完了。

#### ローカル関数 RespondBytesAsync

宣言: `Task RespondBytesAsync( Stream stream, int statusCode, string reason, byte[] body, string contentType, bool headOnly, CancellationToken token )`

- 種類: HandleClientAsync 内の非staticローカル関数。自身にはasync修飾子がない。
- 機能: ログ用応答ステータスを更新し、WriteResponseBytesAsync に委譲する。
- 引数: streamは送信先、statusCodeはHTTPステータス、reasonは理由句、bodyは本文バイト列、contentTypeはMIMEタイプ、headOnlyは本文省略指定、tokenは送信キャンセル通知。
- 戻り値: Task。委譲した送信処理の完了。

### GetContentType( extension )

宣言: `private static string GetContentType( string extension )`

- 種類: private・static・同期メソッド。
- 機能: 拡張子を小文字化し、Content-Type文字列へ変換する。
- 引数: extensionはドットを含む拡張子（例: .html）。
- 戻り値: 下表のMIMEタイプ。未対応拡張子は application/octet-stream。

| 拡張子 | 戻り値 |
| --- | --- |
| .html / .htm | text/html; charset=utf-8 |
| .css | text/css; charset=utf-8 |
| .js | text/javascript; charset=utf-8 |
| .json | application/json; charset=utf-8 |
| .txt | text/plain; charset=utf-8 |
| .xml | application/xml; charset=utf-8 |
| .svg | image/svg+xml |
| .png | image/png |
| .jpg / .jpeg | image/jpeg |
| .gif | image/gif |
| .ico | image/x-icon |
| .pdf | application/pdf |

### WriteResponseAsync( stream, statusCode, reason, body, contentType, headOnly, cancellationToken, extraHeaders )

宣言: `private static Task WriteResponseAsync( Stream stream, int statusCode, string reason, string body, string contentType, bool headOnly, CancellationToken cancellationToken, string extraHeaders = "" )`

- 種類: private・static・非同期処理を返す委譲メソッド。自身にはasync修飾子がない。
- 機能: 文字列本文をUTF-8バイト列へ変換し、WriteResponseBytesAsync に渡す。
- 戻り値: Task。HTTP応答送信の完了。

| 引数 | 型 | 意味 |
| --- | --- | --- |
| stream | Stream | 応答を書き込む通信ストリーム。 |
| statusCode | int | HTTPステータスコード。 |
| reason | string | ステータス行の理由句（例: OK）。 |
| body | string | UTF-8として送信する本文。 |
| contentType | string | Content-Typeヘッダー値。 |
| headOnly | bool | trueなら本文を送らず、本文長を含むヘッダーのみ送信する。 |
| cancellationToken | CancellationToken | 送信処理のキャンセル通知。 |
| extraHeaders | string | 追加ヘッダー文字列。既定は空。指定時は各ヘッダー行をCRLFで終える。 |

### WriteResponseBytesAsync( stream, statusCode, reason, body, contentType, headOnly, cancellationToken, extraHeaders )

宣言: `private static async Task WriteResponseBytesAsync( Stream stream, int statusCode, string reason, byte[] body, string contentType, bool headOnly, CancellationToken cancellationToken, string extraHeaders = "" )`

- 種類: private・static・asyncメソッド。
- 機能: HTTP/1.1ステータス行、Content-Length、Content-Type、Connection: close、追加ヘッダーをASCIIで送る。HEAD以外かつ本文が空でなければ本文を送信し、Flushする。
- 引数: bodyはbyte[]の送信本文。他の引数の型・意味は上のWriteResponseAsyncと同じ。
- 戻り値: Task。ヘッダー・必要な本文の書き込みとFlushの完了。
- 例外・制約: I/O失敗やキャンセル等は呼び出し元へ伝播する。ストリームはここでは破棄しない。ヘッダー値の検証・エスケープは行わない。

### LoadOrCreateCertificate()

宣言: `private static X509Certificate2 LoadOrCreateCertificate()`

- 種類: private・static・同期メソッド。
- 機能: %LOCALAPPDATA%/LHTTPD/localhost.pfx の既存PFXを読み込む。存在しない、または読み込みでCryptographicExceptionが発生した場合は新規作成する。読み込み不能な既存PFXは削除する。
- 引数: なし。
- 戻り値: 秘密鍵付きX509Certificate2。呼び出し側が使用後にDisposeする。
- 証明書内容: RSA 2048ビット、SHA-256、PKCS#1署名、CN=localhost。SANはlocalhostとIPv4 loopback。CAではなくサーバー認証用。作成時UTC時刻の5分前から2年後まで有効。
- 保存・読み込み: パスワードなしPFXとして保存し、UserKeySet | PersistKeySet で再読み込みする。信頼済みストアには登録しない。
- 例外・制約: ディレクトリ作成、削除、保存、暗号処理等の失敗は呼び出し元へ伝播する。既存証明書の期限やSANを独自に検査して更新する処理はない。
## ServerSettings クラス

宣言: `public sealed class ServerSettings（名前空間 LHTTPD）`

定義: [ServerSettings.cs](../GUI_LocalHttpDaemon/ServerSettings.cs)

概要: 配信ルートとHTTP/HTTPSポートを保持し、EXEと同じフォルダの settings.json を読み書きする。CLI版もこのソースをリンクしてコンパイルする。

### フィールド・プロパティ

明示フィールドはない。

| プロパティ | 型・種類 | 説明 |
| --- | --- | --- |
| RootPath | public string、get/set | 配信ルート。初期値は空文字列。 |
| HttpPort | public int、get/set | HTTPポート。初期値80。 |
| HttpsPort | public int、get/set | HTTPSポート。初期値443。 |
| SettingsPath | public static string、getのみ | AppContext.BaseDirectory と settings.json を結合した保存先。作業ディレクトリや単一EXEの一時展開先には依存しない。 |

### ServerSettings()

- 種類: 暗黙のpublicインスタンスコンストラクター。
- 機能: 各プロパティを上記初期値で初期化する。ファイル操作は行わない。
- 引数: なし。
- 戻り値: コンストラクターのためなし。

### Load()

宣言: `public static ServerSettings Load()`

- 種類: public・static・同期メソッド。
- 機能: SettingsPath のJSONを読み込み、デシリアライズする。設定全体またはRootPathがnull、ポートが1～65535以外、両ポートが同値なら JsonException。ファイル未作成の場合だけ既定設定を返す。
- 引数: なし。
- 戻り値: 読み込んだ設定、またはファイル未作成時の新しい既定設定。
- 例外・制約: JSON構文エラー、設定不正、アクセス権・I/Oエラー等は呼び出し元へ伝播する。省略プロパティは初期値を維持する。ルートが空、相対パス、現在存在しない場合も、このメソッドでは拒否しない。

### Save()

宣言: `public void Save()`

- 種類: public・インスタンス・同期メソッド。
- 機能: 現在の設定を整形済みJSONとして同じフォルダの一意な一時ファイルへ書き、File.Move の上書きで設定ファイルを置換する。finallyで一時ファイルが残っていれば削除する。
- 引数: なし。
- 戻り値: void（なし）。
- 例外・制約: 書き込み・移動・削除の例外は呼び出し元へ伝播する。保存前に設定値を検証する処理はない。

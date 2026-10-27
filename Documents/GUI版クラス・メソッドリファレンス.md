# GUI版 クラス・メソッドリファレンス

実装確認日: 2026-10-27

## 対象・読み方

GUI_LocalHttpDaemon（WPF / .NET 10 Windows）の実装リファレンス。クラス概要、全明示フィールド・プロパティ、コンストラクター、public/privateメソッド、名前付きローカル関数を説明する。現状のコードの説明であり、新しい動作仕様を追加するものではない。検証用プロジェクト、.NET/WPFの継承メンバー、コンパイラー生成メンバーの内部実装は対象外。ただし画面理解のため、XAMLから生成される名前付きコントロールと初期化メソッドの役割は記載する。

すべてLHTTPD名前空間に属する。Taskは処理完了、Task<T>は完了時の値を表す。型の末尾の「?」はnull許容。async voidはイベントハンドラーとして呼ばれ、呼び出し元からawaitできない。ソースリンクは本書からの相対パス。

## 目次

- [クラス一覧と処理の流れ](#クラス一覧と処理の流れ)
- [App](#app-クラス)
- [MainWindow](#mainwindow-クラス)
- [AccessLogEntry](#accesslogentry-クラス)
- [LocalHttpServer](#localhttpserver-クラス)
- [ServerSettings](#serversettings-クラス)

## クラス一覧と処理の流れ

| クラス | 役割 | ソース |
| --- | --- | --- |
| App | WPFアプリケーションの起点 | [App.xaml.cs](../GUI_LocalHttpDaemon/App.xaml.cs)、[App.xaml](../GUI_LocalHttpDaemon/App.xaml) |
| MainWindow | 設定入力、開始・停止、ログ表示、終了操作 | [MainWindow.xaml.cs](../GUI_LocalHttpDaemon/MainWindow.xaml.cs)、[MainWindow.xaml](../GUI_LocalHttpDaemon/MainWindow.xaml) |
| AccessLogEntry | ログ1件の文字列と表示色 | [AccessLogEntry.cs](../GUI_LocalHttpDaemon/AccessLogEntry.cs) |
| LocalHttpServer | HTTP/HTTPS待ち受け、接続管理、ファイル配信、証明書管理 | [LocalHttpServer.cs](../GUI_LocalHttpDaemon/LocalHttpServer.cs) |
| ServerSettings | 設定の保持・読み込み・保存 | [ServerSettings.cs](../GUI_LocalHttpDaemon/ServerSettings.cs) |

AppのStartupUri → MainWindow生成・設定復元 → 実行クリック → Validate → 設定保存 → LocalHttpServer生成・Start → AcceptAsync → HandleClientAsync → AccessLogEntry → RecordAccess。

停止クリックまたはウィンドウ終了 → MainWindow.StopAsync → LocalHttpServer.StopAsync → 受付と接続タスクの終了待機・リソース解放。再実行時は新しいLocalHttpServerを生成する。設定は開始前に保存するため、ポート競合で開始できなくても保存値は残る。

## App クラス

宣言: `public partial class App : Application`

定義: [App.xaml.cs](../GUI_LocalHttpDaemon/App.xaml.cs)、[App.xaml](../GUI_LocalHttpDaemon/App.xaml)

概要: WPFアプリケーション。App.xamlのStartupUriにMainWindow.xamlを指定し、起動時にメイン画面を表示する。コードビハインドには明示フィールド、プロパティ、コンストラクター、メソッド、イベントハンドラーを定義していない。

### App() と生成メンバー

- App()は暗黙のpublicインスタンスコンストラクター。引数なし、戻り値なし。
- WPFビルドで生成されるMain・InitializeComponent等がアプリ起動とXAML読み込みを担う。生成コードの内部は本書の対象外。
- [AssemblyInfo.cs](../GUI_LocalHttpDaemon/AssemblyInfo.cs)はThemeInfo属性のみを定義し、独自クラス・メソッドはない。

## MainWindow クラス

宣言: `public partial class MainWindow : Window`

定義: [MainWindow.xaml.cs](../GUI_LocalHttpDaemon/MainWindow.xaml.cs)、[MainWindow.xaml](../GUI_LocalHttpDaemon/MainWindow.xaml)

概要: 入力検証、サーバー操作、エラー表示、アクセスログ保持、終了制御を担当するメインウィンドウ。起動直後には待ち受けを開始しない。

### フィールド・プロパティ

| フィールド | 型・修飾子 | 用途 |
| --- | --- | --- |
| MaximumLogEntries | private const int | 1000。保持するログ件数の上限。 |
| m_accessLogs | private readonly ObservableCollection<AccessLogEntry> | ログ一覧のItemsSource。古い順に保持し、表示切替・停止・再実行では消去しない。 |
| m_server | private LocalHttpServer? | 現在のサーバー。未開始／停止時はnull。 |
| m_closing | private bool | 停止完了後の再Closeで終了処理を繰り返さないためのフラグ。初期値false。 |
| m_busy | private bool | 開始・停止操作の多重実行防止。初期値false。 |

独自の明示プロパティはない。Window由来のIsEnabled、Dispatcher等は継承メンバー。

### XAMLで定義する名前付きコントロール

以下はXAMLから生成されるインスタンスフィールド。手書きC#のフィールドとは区別する。

| 名前 | 型 | 用途 |
| --- | --- | --- |
| SettingsPanel | StackPanel | 設定欄全体。動作中に無効化する。 |
| RootPath | TextBox | ルートパスの入力・復元。 |
| HttpPort | TextBox | HTTPポート入力。初期表示80、最大5桁。 |
| HttpsPort | TextBox | HTTPSポート入力。初期表示443、最大5桁。 |
| LogToggleButton | Button | ログ表示／隠蔽ボタン。 |
| RunButton | Button | 実行／停止ボタン。操作中は無効。 |
| AccessLog | ListBox | ログ一覧。初期状態Collapsed。項目のTextとForegroundにバインドする。 |

### MainWindow()

宣言: `public MainWindow()`

- 種類: publicインスタンスコンストラクター。
- 機能: InitializeComponentでXAMLを読み、AccessLog.ItemsSourceを設定して、ServerSettings.Loadの値を入力欄へ復元する。
- 引数: なし。
- 戻り値: コンストラクターのためなし。
- エラー処理: IOException、UnauthorizedAccessException、JsonExceptionはLoadedイベントで通知する。入力欄はXAMLの既定表示を維持する。その他の例外は伝播する。

### LogToggle_Click( sender, e )

宣言: `private void LogToggle_Click( object sender, RoutedEventArgs e )`

- 種類: private・インスタンス・同期イベントハンドラー。
- 機能: AccessLogのVisible/Collapsedを切り替え、ボタン表示を「▼ログ表示」「▲ログ隠蔽」にする。表示時のログ高さはMath.Max(100, ActualHeight - AccessLog.Margin.Top)。SizeToContentによって画面高が内容へ追従する。
- 引数: senderはイベント発生元、eはクリックのルーティングイベント情報。いずれも処理内では参照しない。
- 戻り値: void（なし）。

### RecordAccess( entry )

宣言: `private void RecordAccess( AccessLogEntry entry )`

- 種類: private・インスタンス・同期メソッド／サーバーへのログコールバック。
- 機能: UIスレッドなら直接、それ以外ならDispatcher.InvokeAsyncでログ追加を予約する。
- 引数: entryは追加するログ1件。
- 戻り値: void（なし）。Dispatcherへ予約した場合は追加完了を待たず戻る。

#### ローカル関数 AppendEntry

宣言: `void AppendEntry()`

- 種類: RecordAccess内の非static・同期ローカル関数。
- 機能: 件数が1000以上なら最古の1件を削除し、捕捉したentryを末尾に追加する。
- 引数: なし（外側のentryとインスタンスを参照する）。
- 戻り値: void（なし）。

### Browse_Click( sender, e )

宣言: `private void Browse_Click( object sender, RoutedEventArgs e )`

- 種類: private・インスタンス・同期イベントハンドラー。
- 機能: 単一フォルダ選択のOpenFolderDialogを表示し、確定した場合だけRootPath.Textに選択結果を反映する。
- 引数: senderはイベント発生元、eはクリックイベント情報。処理内では参照しない。
- 戻り値: void（なし）。

### IsDigits( text )

宣言: `private static bool IsDigits( string text )`

- 種類: private・static・同期メソッド。
- 機能: すべての文字が半角数字0～9か判定する。
- 引数: textは検証対象文字列。
- 戻り値: 全文字が半角数字ならtrue、それ以外はfalse。空文字列もAllの動作によりtrueとなる。必須・範囲検証はValidateで別途行う。

### Port_PreviewTextInput( sender, e )

宣言: `private void Port_PreviewTextInput( object sender, TextCompositionEventArgs e )`

- 種類: private・インスタンス・同期イベントハンドラー。
- 機能: 入力文字が半角数字のみでなければe.Handledをtrueにして入力を抑止する。
- 引数: senderは対象ポート欄（処理内未参照）、eは入力文字e.Textと処理済みフラグを持つイベント情報。
- 戻り値: void（なし）。

### Port_Pasting( sender, e )

宣言: `private void Port_Pasting( object sender, DataObjectPastingEventArgs e )`

- 種類: private・インスタンス・同期イベントハンドラー。
- 機能: Unicode文字列の貼り付けデータがない、文字列として取得できない、半角数字以外を含む場合にCancelCommandで貼り付けを中止する。
- 引数: senderは対象ポート欄（処理内未参照）、eは貼り付けデータと中止操作を提供するイベント情報。
- 戻り値: void（なし）。

### ShowError( message, target )

宣言: `private void ShowError( string message, Control? target = null )`

- 種類: private・インスタンス・同期メソッド。
- 機能: 所有ウィンドウ付きのエラーダイアログ（タイトル「入力・実行エラー」）を表示し、閉じた後に指定コントロールへフォーカスする。
- 引数: messageは表示文、targetはフォーカス対象。省略時null。
- 戻り値: void（なし）。

### Validate( out root, out httpPort, out httpsPort )

宣言: `private bool Validate( out string root, out int httpPort, out int httpsPort )`

- 種類: private・インスタンス・同期メソッド。
- 機能: ルート、HTTP、HTTPSの順に検証し、最初のエラーをShowErrorで通知する。ルートは必須・存在する絶対パス、両ポートは半角数字・1～65535・相互に異なる値であることを要求する。
- 戻り値: 全検証成功ならtrue、通知して中断したらfalse。

| 引数 | 型・方向 | 意味 |
| --- | --- | --- |
| root | out string | 前後の空白を除いた入力パス。存在・絶対パス検証後にGetFullPathで正規化する。 |
| httpPort | out int | HTTPポート。初期値0、解析が成功した段階で数値を格納する。 |
| httpsPort | out int | HTTPSポート。初期値0、解析が成功した段階で数値を格納する。 |

false時のout値は途中状態なので起動に使用しない。Path処理等の想定外例外は自身では捕捉せず、実行ハンドラーへ伝播する。

### Run_ClickAsync( sender, e )

宣言: `private async void Run_ClickAsync( object sender, RoutedEventArgs e )`

- 種類: private・インスタンス・async voidイベントハンドラー。
- 機能: m_busyで多重操作を抑止し、実行ボタンを無効化する。既にサーバーがあればStopAsync、なければValidate、設定保存、LocalHttpServer生成、Startを行う。成功時に設定パネルを無効化しボタンを「停止」にする。
- 引数: senderはイベント発生元、eはクリックイベント情報。処理内では参照しない。
- 戻り値: void（なし）。呼び出し元はawaitできない。
- エラー処理: 設定保存時のI/O・権限エラーを専用メッセージで通知する。その他の操作例外もダイアログで通知する。finallyでm_busyと実行ボタンの有効状態を戻す。

### StopAsync()

宣言: `private async Task StopAsync()`

- 種類: private・インスタンス・asyncメソッド。
- 機能: m_serverを退避してnullにし、存在していたサーバーのStopAsyncをawaitする。finallyで設定パネルを有効化し、ボタンを「実行」に戻す。
- 引数: なし。
- 戻り値: Task。サーバー停止とUI復元の完了。
- 例外: サーバー停止の例外はUI復元後に呼び出し元へ伝播する。

### Window_ClosingAsync( sender, e )

宣言: `private async void Window_ClosingAsync( object? sender, CancelEventArgs e )`

- 種類: private・インスタンス・async voidイベントハンドラー。
- 機能: 停止待ちが必要なら一度終了をキャンセルし、画面を無効化してStopAsyncを待つ。最後にm_closingをtrueとしてCloseを再実行する。既にm_closingならそのまま終了する。
- 引数: senderは終了イベント発生元（null許容、処理内未参照）、eは終了を取り消すCancelフラグを持つイベント情報。
- 戻り値: void（なし）。
- 制約: m_busyの操作中に閉じた場合、その終了要求をキャンセルして戻る。停止例外はダイアログ表示後にCloseへ進む。

## AccessLogEntry クラス

宣言: `public sealed class AccessLogEntry`

定義: [AccessLogEntry.cs](../GUI_LocalHttpDaemon/AccessLogEntry.cs)

概要: アクセスログ1件を画面表示用の文字列とブラシに変換する変更不可のデータオブジェクト。

### フィールド・プロパティ

明示フィールドはない。

| プロパティ | 型・種類 | 説明 |
| --- | --- | --- |
| Text | public string、getのみ | 「[メソッド] yyyy/MM/dd HH:mm 結果 パス」。エラーがあれば「 — 詳細」を付加。コンストラクターで確定する。 |
| Foreground | public Brush、getのみ | 行全体の色。Brushes.Red / Yellow / White。コンストラクターで確定する。 |

### AccessLogEntry( method, path, timestamp, statusCode, error )

宣言: `public AccessLogEntry( string method, string path, DateTime timestamp, int statusCode, string? error = null )`

- 種類: publicインスタンスコンストラクター。
- 機能: 文字列・表示色を作成する。パス先頭の/を除き、空になれば/を表示する。ステータス0はERRORと表示。文字列内の制御文字は空白に置換する。
- 戻り値: コンストラクターのためなし。

| 引数 | 型 | 意味 |
| --- | --- | --- |
| method | string | 要求メソッド。未解析時はERROR。 |
| path | string | 要求パス。通常はクエリ・フラグメント除去済み。URLのパーセント表記は維持する。 |
| timestamp | DateTime | 接続処理開始時のローカル時刻。 |
| statusCode | int | 記録済み応答コード。0なら応答コード未設定。 |
| error | string? | エラー詳細。既定null。 |

色の優先順位は、errorが非null・404・500以上なら赤、次にGET以外・300～399なら黄、残る400以上なら赤、それ以外は白。POSTへの405とHEADは黄。色分類のための3xx判定はあるが、サーバー自身はリダイレクト応答を生成しない。

#### ローカル関数 SingleLine

宣言: `static string SingleLine( string value )`

- 種類: コンストラクター内のstatic・同期ローカル関数。
- 機能: 制御文字を空白へ置換し、ログを1行に収める。
- 引数: valueは整形対象文字列。
- 戻り値: 置換後の新しい文字列。

## LocalHttpServer クラス

宣言: `public sealed class LocalHttpServer`

定義: [LocalHttpServer.cs](../GUI_LocalHttpDaemon/LocalHttpServer.cs)

概要: HTTP/HTTPSの待ち受け、接続タスク管理、静的ファイル配信、証明書管理を担当する。画面へのログ表示はコールバックへ委譲する。UI操作は行わない。

### フィールド・プロパティ

| フィールド | 型・修飾子 | 用途 |
| --- | --- | --- |
| m_accessLog | private readonly Action<AccessLogEntry>? | ログ通知先。nullなら通知しない。 |
| MaximumHeaderBytes | private const int | 32768。受信ヘッダー蓄積の上限判定値。 |
| m_shutdown | private readonly CancellationTokenSource | 受付・接続処理の停止通知。生成時に初期化。 |
| m_connections | private readonly List<Task> | 接続処理タスク。新規受付時に完了済みを除き、停止時の待機に使用。 |
| m_http | private readonly TcpListener | IPv4 loopbackのHTTPリスナー。 |
| m_https | private readonly TcpListener | IPv4 loopbackのHTTPSリスナー。 |
| m_root | private readonly string | 配信ルート。コンストラクターで保持。 |
| m_certificate | private X509Certificate2? | Startで準備する証明書。開始前はnull、停止時等に破棄。 |
| m_acceptTask | private Task? | HTTP/HTTPS両受付処理のWhenAll。開始前はnull。 |

明示プロパティはない。

### LocalHttpServer( root, httpPort, httpsPort, accessLog )

宣言: `public LocalHttpServer( string root, int httpPort, int httpsPort, Action<AccessLogEntry>? accessLog = null )`

- 種類: publicインスタンスコンストラクター。
- 機能: ルートと通知先を保持し、両リスナーを生成する。待ち受けは開始しない。
- 戻り値: コンストラクターのためなし。

| 引数 | 型 | 意味 |
| --- | --- | --- |
| root | string | 配信ルート。画面側で絶対パス・存在を検証して渡す。 |
| httpPort | int | HTTP待ち受けポート。 |
| httpsPort | int | HTTPS待ち受けポート。 |
| accessLog | Action<AccessLogEntry>? | ログ1件を受け取るコールバック。既定null。UIスレッドへの切替は通知先で行う。 |

このクラス自身には画面と同等の入力検証はない。TcpListener生成時の引数例外等は呼び出し元へ伝播する。

### Start()

宣言: `public void Start()`

- 種類: public・インスタンス・同期メソッド。
- 機能: 証明書を準備し、HTTP、HTTPSの順に開始して、両AcceptAsyncをTask.WhenAllにまとめm_acceptTaskへ保持する。
- 引数: なし。
- 戻り値: void（なし）。受付ループ終了を待たず戻る。
- エラー処理: 失敗時は両リスナーを止め、証明書と停止トークンソースを破棄して再throwする。
- 制約: 同じインスタンスの再Startや多重Startを管理する状態チェックはない。画面は実行ごとに新しいインスタンスを生成する。

### StopAsync()

宣言: `public async Task StopAsync()`

- 種類: public・インスタンス・asyncメソッド。
- 機能: 停止通知をキャンセルして両リスナーを停止し、受付タスク、保持中の接続タスクの順にawaitする。finallyで証明書と停止トークンソースを破棄する。
- 引数: なし。
- 戻り値: Task。停止処理の完了。
- 例外・制約: 待機対象の失敗は呼び出し元へ伝播する。受付タスクが失敗した場合、後続の接続タスク待機へは進まないがfinallyの破棄は実行する。停止トークンソースを破棄するため、同一インスタンスで繰り返し停止・再開するAPIではない。

### AcceptAsync( listener, useTls )

宣言: `private async Task AcceptAsync( TcpListener listener, bool useTls )`

- 種類: private・インスタンス・asyncメソッド。
- 機能: 停止まで接続を受け付ける。m_connectionsから完了済みタスクを除き、新しいHandleClientAsyncのタスクを追加する。接続処理はawaitせず受付を続ける。
- 引数: listenerは開始済みTcpListener、useTlsは受けた接続でTLSを使用するかを表すbool。
- 戻り値: Task。受付ループの終了。
- エラー処理: 停止通知済みのOperationCanceledException・SocketExceptionを正常な停止として扱う。他の例外は呼び出し元へ伝播する。

### HandleClientAsync( client, useTls, certificate, root, cancellationToken )

宣言: `private async Task HandleClientAsync( TcpClient client, bool useTls, X509Certificate2 certificate, string root, CancellationToken cancellationToken )`

- 種類: private・インスタンス・asyncメソッド。
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
6. ルート配下の各パス要素を検査し、コロンを含む要素や存在する再解析ポイントを403で拒否する。GET/POST要求はTraceにも出力する。
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
- 機能: %LOCALAPPDATA%/LHTTPD/GUI/localhost.pfx の既存PFXを読み込む。存在しない、または読み込みでCryptographicExceptionが発生した場合は新規作成する。読み込み不能な既存PFXは削除する。
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

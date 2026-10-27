#pragma warning disable IDE0130

using System ;
using System.Globalization ;
using System.Net ;
using System.Net.Sockets ;
using System.Security.Cryptography ;
using System.Security.Cryptography.X509Certificates ;
using System.Text ;

namespace LHTTPD
{
	/// <summary>
	/// コマンドライン引数と保存設定に基づき HTTP・HTTPS サーバーを実行します。
	/// </summary>
	public class Program
	{
		// 既定の HTTP 待ち受けポート番号。
		private const int DefaultHttpPort = 80 ;

		// 既定の HTTPS 待ち受けポート番号。
		private const int DefaultHttpsPort = 443 ;

		// 受信を許可するリクエストヘッダーの最大バイト数。
		private const int MaximumHeaderBytes = 32768 ;

		// コンソールログの色と出力を排他制御するロック。
		private static readonly object m_logLock = new() ;

		/// <summary>
		/// 設定を検証し、終了要求までサーバーを実行します。
		/// </summary>
		/// <param name="args">公開ルートや待ち受けポートを指定するコマンドライン引数。</param>
		/// <returns>正常終了は 0、実行エラーは 1、引数や公開ルートのエラーは 2。</returns>
		public static async Task<int> Main( string[] args )
		{
			Console.OutputEncoding = new UTF8Encoding( false ) ;
			if( args.Any( argument => argument is "--help" or "-h" ) )
			{
				PrintUsage() ;
				return 0 ;
			}

			ServerSettings settings ;
			try
			{
				settings = File.Exists( ServerSettings.SettingsPath ) ? ServerSettings.Load() :
					new ServerSettings { HttpPort = DefaultHttpPort, HttpsPort = DefaultHttpsPort } ;
			}
			catch( Exception ex )
			{
				Console.Error.WriteLine( $"settings.json を読み込めません。既定値を使用します: {ex.Message}" ) ;
				settings = new ServerSettings { HttpPort = DefaultHttpPort, HttpsPort = DefaultHttpsPort } ;
			}
			var root = settings.RootPath ;
			var httpPort = settings.HttpPort ;
			var httpsPort = settings.HttpsPort ;

			for( var i = 0 ; i < args.Length ; i ++ )
			{
				if( args[ i ] == "--root" && i + 1 < args.Length )
				{
					root = NormalizePathSeparators( args[ ++ i ] ) ;

					if( !Path.IsPathFullyQualified( root ) )
					{
						Console.Error.WriteLine( "--root には絶対パスを指定してください。" ) ;
						return 2 ;
					}
				}
				else if( args[ i ] == "--port" && i + 1 < args.Length )
				{
					var ports = args[ ++ i ].Split( '/' ) ;

					if( ports.Length is < 1 or > 2 ||
						!int.TryParse( ports[ 0 ], out httpPort ) ||
						( ports.Length == 2 && ports[ 1 ].Length > 0 && !int.TryParse( ports[ 1 ], out httpsPort ) ) ||
						httpPort is < 1 or > 65535 ||
						httpsPort is < 1 or > 65535 ||
						httpPort == httpsPort )
					{
						Console.Error.WriteLine( "--port は有効な HTTP ポート、または異なる HTTP/HTTPS ポートを指定してください（例: 80 または 80/443）。" ) ;
						return 2 ;
					}
				}
				else
				{
					Console.Error.WriteLine( $"不明または値が不足している引数です: {args[ i ]}" ) ;
					PrintUsage() ;
					return 2 ;
				}
			}

			if( string.IsNullOrWhiteSpace( root ) )
			{
				Console.WriteLine( "--root で、HTMLファイルの格納されたルートフォルダを指定してください。" ) ;
				PrintUsage() ;
				return 0 ;
			}
			root = NormalizePathSeparators( root ) ;
			if( !Path.IsPathFullyQualified( root ) )
			{
				Console.Error.WriteLine( "ルートフォルダには絶対パスを指定してください。" ) ;
				return 2 ;
			}
			try
			{
				root = Path.GetFullPath( root ) ;
			}
			catch( Exception ex ) when( ex is ArgumentException or NotSupportedException or PathTooLongException )
			{
				Console.Error.WriteLine( $"ルートフォルダのパスが不正です: {ex.Message}" ) ;
				return 2 ;
			}

			if( !Directory.Exists( root ) )
			{
				Console.Error.WriteLine( $"ルートフォルダが見つかりません: {root}" ) ;
				return 2 ;
			}

			X509Certificate2 certificate ;
			try
			{
				certificate = LoadOrCreateCertificate() ;
			}
			catch( Exception ex )
			{
				Console.Error.WriteLine( $"HTTPS 証明書を準備できません: {ex.Message}" ) ;
				return 1 ;
			}

			using( certificate )
			using( var shutdown = new CancellationTokenSource() )
			{
				Console.CancelKeyPress += ( _, e ) =>
				{
					e.Cancel = true ;
					shutdown.Cancel() ;
				} ;

				var httpListener = new TcpListener( IPAddress.Loopback, httpPort ) ;
				var httpsListener = new TcpListener( IPAddress.Loopback, httpsPort ) ;

				try
				{
					httpListener.Start() ;
					httpsListener.Start() ;
				}
				catch( SocketException ex )
				{
					httpListener.Stop() ;
					httpsListener.Stop() ;
					Console.Error.WriteLine( $"ポートを開けませんでした: {ex.Message}" ) ;
					Console.Error.WriteLine( "ポート 80 の利用には管理者権限が必要な場合があります。" ) ;
					return 1 ;
				}

				try
				{
					if( !File.Exists( ServerSettings.SettingsPath ) || settings.RootPath != root ||
						settings.HttpPort != httpPort || settings.HttpsPort != httpsPort )
					{
						new ServerSettings { RootPath = root, HttpPort = httpPort, HttpsPort = httpsPort }.Save() ;
					}
				}
				catch( Exception ex )
				{
					httpListener.Stop() ;
					httpsListener.Stop() ;
					Console.Error.WriteLine( $"settings.json を保存できません: {ex.Message}" ) ;
					return 1 ;
				}

				Console.WriteLine( $"ROOT : {root}" ) ;
				Console.WriteLine( $"HTTP : http://localhost:{httpPort}/" ) ;
				Console.WriteLine( $"HTTPS: https://localhost:{httpsPort}/" ) ;
				Console.WriteLine( "終了するには Ctrl+C を押してください。" ) ;
				Console.WriteLine( "HTTPS 証明書は自己署名です。信頼されていない場合、ブラウザに警告が表示されます。" ) ;

				try
				{
					await Task.WhenAll
					(
						AcceptLoopAsync( httpListener, false, certificate, root, shutdown.Token ),
						AcceptLoopAsync( httpsListener, true, certificate, root, shutdown.Token )
					) ;
				}
				catch( OperationCanceledException )
				{
					// Ctrl+C による正常終了
				}
				finally
				{
					httpListener.Stop() ;
					httpsListener.Stop() ;
				}
			}

			return 0 ;
		}

		// コマンドラインオプションの使い方を表示する。
		private static void PrintUsage()
		{
			Console.WriteLine( "コマンドラインオプション:" ) ;
			Console.WriteLine( "--root <絶対パス> HTMLファイルの格納されたルートフォルダを絶対パスで指定します。" ) ;
			Console.WriteLine( "--port <HTTPポート番号>[/<HTTPSポート番号>] 待ち受けポートを指定します。HTTPS省略時はHTTPのみ更新します。" ) ;
			Console.WriteLine( "--help, -h このヘルプを表示します。保存済みの設定は引数で上書きできます。" ) ;
		}

		// パス区切り文字を実行環境の形式に統一する。
		private static string NormalizePathSeparators( string path )
		{
			return path.Replace( '/', Path.DirectorySeparatorChar ).Replace( '\\', Path.DirectorySeparatorChar ) ;
		}

		// 停止要求まで接続を受け付け、各リクエストの処理を開始する。
		private static async Task AcceptLoopAsync
		(
			TcpListener listener,
			bool useTls,
			X509Certificate2 certificate,
			string root,
			CancellationToken cancellationToken
		)
		{
			while( !cancellationToken.IsCancellationRequested )
			{
				TcpClient client ;
				try
				{
					client = await listener.AcceptTcpClientAsync( cancellationToken ) ;
				}
				catch( OperationCanceledException )
				{
					break ;
				}
				catch( SocketException ) when( cancellationToken.IsCancellationRequested )
				{
					break ;
				}

				_ = HandleClientAsync( client, useTls, certificate, root, cancellationToken ) ;
			}
		}

		// 接続の暗号化と要求の検証を行い、ファイル応答とアクセスログを生成する。
		private static async Task HandleClientAsync
		(
			TcpClient client,
			bool useTls,
			X509Certificate2 certificate,
			string root,
			CancellationToken cancellationToken
		)
		{
			using( client )
			using( var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken ) )
			{
				timeout.CancelAfter( TimeSpan.FromSeconds( 30 ) ) ;
				var shutdownToken = cancellationToken ;
				cancellationToken = timeout.Token ;
				var timestamp = DateTime.Now ;
				var method = "ERROR" ;
				var requestPath = "/" ;
				var responseStatus = 0 ;
				string? error = null ;

				// 応答ステータスを記録し、文字列の HTTP 応答を送信する。
				Task RespondAsync( Stream stream, int statusCode, string reason, string body, string contentType,
					bool headOnly, CancellationToken token, string extraHeaders = "" )
				{
					responseStatus = statusCode ;
					return WriteResponseAsync( stream, statusCode, reason, body, contentType, headOnly, token, extraHeaders ) ;
				}

				// 応答ステータスを記録し、バイト列の HTTP 応答を送信する。
				Task RespondBytesAsync( Stream stream, int statusCode, string reason, byte[] body, string contentType,
					bool headOnly, CancellationToken token )
				{
					responseStatus = statusCode ;
					return WriteResponseBytesAsync( stream, statusCode, reason, body, contentType, headOnly, token ) ;
				}
				try
				{
					Stream stream = client.GetStream() ;

					if( useTls )
					{
						var sslStream = new System.Net.Security.SslStream( stream ) ;
						await sslStream.AuthenticateAsServerAsync
						(
							new System.Net.Security.SslServerAuthenticationOptions
							{
								ServerCertificate = certificate,
								ClientCertificateRequired = false,
								EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
								CertificateRevocationCheckMode = X509RevocationMode.NoCheck
							},
							cancellationToken
						) ;
						stream = sslStream ;
					}

					var header = new List<byte> () ;
					var buffer = new byte[ 2048 ] ;

					while( header.Count < MaximumHeaderBytes )
					{
						var bytesRead = await stream.ReadAsync( buffer, cancellationToken ) ;

						if( bytesRead == 0 )
						{
							return ;
						}

						header.AddRange( buffer.AsSpan( 0, bytesRead ).ToArray() ) ;

						if( Encoding.ASCII.GetString( header.ToArray() ).Contains( "\r\n\r\n", StringComparison.Ordinal ) )
						{
							break ;
						}
					}

					if( header.Count >= MaximumHeaderBytes )
					{
						await RespondAsync( stream, 431, "Request Header Fields Too Large", "リクエストヘッダーが大きすぎます。", "text/plain; charset=utf-8", false, cancellationToken ) ;
						return ;
					}

					var requestLine = Encoding.ASCII.GetString( header.ToArray() ).Split( "\r\n" )[ 0 ].Split( ' ' ) ;
					if( requestLine.Length < 3 || requestLine[ 2 ] is not ( "HTTP/1.0" or "HTTP/1.1" ) )
					{
						await RespondAsync( stream, 400, "Bad Request", "不正な HTTP リクエストです。", "text/plain; charset=utf-8", false, cancellationToken ) ;
						return ;
					}

					var rawPath = requestLine[ 1 ].Split( '?' )[ 0 ].Split( '#' )[ 0 ] ;
					method = requestLine[ 0 ] ;
					requestPath = rawPath ;

					var headOnly = requestLine[ 0 ] == "HEAD" ;
					if( requestLine[ 0 ] is not ( "GET" or "HEAD" ) )
					{
						await RespondAsync( stream, 405, "Method Not Allowed", "GET と HEAD のみ対応しています。", "text/plain; charset=utf-8", headOnly, cancellationToken, "Allow: GET, HEAD\r\n" ) ;
						return ;
					}

					string relativePath ;
					try
					{
						relativePath = Uri.UnescapeDataString( rawPath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) ) ;
					}
					catch( UriFormatException )
					{
						await RespondAsync( stream, 400, "Bad Request", "不正な URL です。", "text/plain; charset=utf-8", headOnly, cancellationToken ) ;
						return ;
					}

					if( relativePath.Length == 0 )
					{
						relativePath = "index.html" ;
					}

					var rootFullPath = Path.GetFullPath( root ) ;
					var filePath = Path.GetFullPath( Path.Combine( rootFullPath, relativePath ) ) ;
					var rootPrefix = rootFullPath.TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar ;

					if( !filePath.StartsWith( rootPrefix, StringComparison.OrdinalIgnoreCase ) &&
						!string.Equals( filePath, rootFullPath, StringComparison.OrdinalIgnoreCase ) )
					{
						await RespondAsync( stream, 403, "Forbidden", "アクセスできません。", "text/plain; charset=utf-8", headOnly, cancellationToken ) ;
						return ;
					}

					if( Directory.Exists( filePath ) )
					{
						filePath = Path.Combine( filePath, "index.html" ) ;
					}

					if( !File.Exists( filePath ) )
					{
						await RespondAsync( stream, 404, "Not Found", "ファイルが見つかりません。", "text/plain; charset=utf-8", headOnly, cancellationToken ) ;
						return ;
					}

					await RespondBytesAsync
					(
						stream,
						200,
						"OK",
						await File.ReadAllBytesAsync( filePath, cancellationToken ),
						GetContentType( Path.GetExtension( filePath ) ),
						headOnly,
						cancellationToken
					) ;
				}
				catch( OperationCanceledException )
				{
					if( !shutdownToken.IsCancellationRequested )
					{
						error = "接続がタイムアウトしました。" ;
					}
				}
				catch( IOException ex )
				{
					error = $"通信・ファイル処理エラー: {ex.Message}" ;
				}
				catch( System.Security.Authentication.AuthenticationException ex )
				{
					error = $"HTTPS接続エラー: {ex.Message}" ;
				}
				catch( Exception ex )
				{
					error = $"リクエスト処理エラー: {ex.Message}" ;
				}
				finally
				{
					if( !shutdownToken.IsCancellationRequested && ( responseStatus != 0 || error != null ) )
					{
						WriteAccessLog( method, requestPath, timestamp, responseStatus, error ) ;
					}
				}
			}
		}

		// 要求と応答結果を制御文字のない色付きログとして出力する。
		private static void WriteAccessLog( string method, string path, DateTime timestamp, int statusCode, string? error )
		{
			// ログ文字列の制御文字を空白に置換して単一行にする。
			static string SingleLine( string value )
			{
				return new string( value.Select( character => char.IsControl( character ) ? ' ' : character ).ToArray() ) ;
			}
			var displayPath = SingleLine( path.TrimStart( '/' ) ) ;
			if( displayPath.Length == 0 )
			{
				displayPath = "/" ;
			}
			var status = statusCode == 0 ? "ERROR" : statusCode.ToString( CultureInfo.InvariantCulture ) ;
			var text = $"[{SingleLine( method )}] {timestamp.ToString( "yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture )} {status} {displayPath}" ;
			if( error != null )
			{
				text += $" — {SingleLine( error )}" ;
			}

			// GUI版と同じ優先順位で、未対応メソッドの405は黄色にする。
			var color = error != null || statusCode == 404 || statusCode >= 500 ? ConsoleColor.Red :
				method != "GET" || statusCode is >= 300 and < 400 ? ConsoleColor.Yellow :
				statusCode >= 400 ? ConsoleColor.Red : ConsoleColor.White ;
			lock( m_logLock )
			{
				var previousColor = Console.ForegroundColor ;
				try
				{
					Console.ForegroundColor = color ;
					Console.WriteLine( text ) ;
				}
				finally
				{
					Console.ForegroundColor = previousColor ;
				}
			}
		}

		// 拡張子に対応する MIME タイプを返す。
		private static string GetContentType( string extension )
		{
			return extension.ToLowerInvariant() switch
			{
				".html" or ".htm" => "text/html; charset=utf-8",
				".css" => "text/css; charset=utf-8",
				".js" => "text/javascript; charset=utf-8",
				".json" => "application/json; charset=utf-8",
				".txt" => "text/plain; charset=utf-8",
				".xml" => "application/xml; charset=utf-8",
				".svg" => "image/svg+xml",
				".png" => "image/png",
				".jpg" or ".jpeg" => "image/jpeg",
				".gif" => "image/gif",
				".ico" => "image/x-icon",
				".pdf" => "application/pdf",
				_ => "application/octet-stream"
			} ;
		}

		// 文字列の本文を UTF-8 に変換して HTTP 応答を送信する。
		private static Task WriteResponseAsync
		(
			Stream stream,
			int statusCode,
			string reason,
			string body,
			string contentType,
			bool headOnly,
			CancellationToken cancellationToken,
			string extraHeaders = ""
		)
		{
			return WriteResponseBytesAsync
			(
				stream,
				statusCode,
				reason,
				Encoding.UTF8.GetBytes( body ),
				contentType,
				headOnly,
				cancellationToken,
				extraHeaders
			) ;
		}

		// HTTP ヘッダーとバイト列の本文を送信し、HEAD では本文を省略する。
		private static async Task WriteResponseBytesAsync
		(
			Stream stream,
			int statusCode,
			string reason,
			byte[] body,
			string contentType,
			bool headOnly,
			CancellationToken cancellationToken,
			string extraHeaders = ""
		)
		{
			var responseHeaders = Encoding.ASCII.GetBytes
			(
				$"HTTP/1.1 {statusCode} {reason}\r\n" +
				$"Content-Length: {body.Length}\r\n" +
				$"Content-Type: {contentType}\r\n" +
				$"Connection: close\r\n{extraHeaders}\r\n"
			) ;

			await stream.WriteAsync( responseHeaders, cancellationToken ) ;

			if( !headOnly && body.Length > 0 )
			{
				await stream.WriteAsync( body, cancellationToken ) ;
			}

			await stream.FlushAsync( cancellationToken ) ;
		}

		// 保存済みの localhost 証明書を読み込み、利用できない場合は自己署名証明書を生成する。
		private static X509Certificate2 LoadOrCreateCertificate()
		{
			var folder = Path.Combine
			(
				Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ),
				"LHTTPD"
			) ;
			Directory.CreateDirectory( folder ) ;

			var certificatePath = Path.Combine( folder, "localhost.pfx" ) ;
			if( File.Exists( certificatePath ) )
			{
				try
				{
					return X509CertificateLoader.LoadPkcs12FromFile
					(
						certificatePath,
						null,
						X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet
					) ;
				}
				catch( CryptographicException )
				{
					// 読み込めない証明書は破棄し、新しい証明書を作成する
					File.Delete( certificatePath ) ;
				}
			}

			using var key = RSA.Create( 2048 ) ;
			var certificateRequest = new CertificateRequest
			(
				"CN=localhost",
				key,
				HashAlgorithmName.SHA256,
				RSASignaturePadding.Pkcs1
			) ;
			var subjectAlternativeName = new SubjectAlternativeNameBuilder() ;
			subjectAlternativeName.AddDnsName( "localhost" ) ;
			subjectAlternativeName.AddIpAddress( IPAddress.Loopback ) ;
			certificateRequest.CertificateExtensions.Add( subjectAlternativeName.Build() ) ;
			certificateRequest.CertificateExtensions.Add( new X509BasicConstraintsExtension( false, false, 0, true ) ) ;
			certificateRequest.CertificateExtensions.Add
			(
				new X509KeyUsageExtension
				(
					X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
					true
				)
			) ;

			var enhancedKeyUsages = new OidCollection { new Oid( "1.3.6.1.5.5.7.3.1" ) } ;
			certificateRequest.CertificateExtensions.Add( new X509EnhancedKeyUsageExtension( enhancedKeyUsages, true ) ) ;

			using var generatedCertificate = certificateRequest.CreateSelfSigned
			(
				DateTimeOffset.UtcNow.AddMinutes( -5 ),
				DateTimeOffset.UtcNow.AddYears( 2 )
			) ;
			File.WriteAllBytes( certificatePath, generatedCertificate.Export( X509ContentType.Pfx ) ) ;

			return X509CertificateLoader.LoadPkcs12FromFile
			(
				certificatePath,
				null,
				X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet
			) ;
		}
	}
}

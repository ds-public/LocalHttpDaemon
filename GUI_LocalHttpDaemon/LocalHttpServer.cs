using System.IO ;
using System.Net ;
using System.Net.Sockets ;
using System.Security.Cryptography ;
using System.Security.Cryptography.X509Certificates ;
using System.Text ;

namespace LHTTPD
{
	/// <summary>
	/// ループバックアドレスで HTTP・HTTPS によるコンテンツ配信を行います。
	/// </summary>
	public sealed class LocalHttpServer
	{
		// 要求処理の結果を通知する任意のログコールバック。
		private readonly Action<AccessLogEntry>? m_accessLog ;
		// 受信を許可するリクエストヘッダーの最大バイト数。
		private const int MaximumHeaderBytes = 32768 ;
		// 待ち受けと接続処理へ停止要求を伝えるキャンセルトークンソース。
		private readonly CancellationTokenSource m_shutdown = new() ;
		// 停止時に完了を待つ接続処理の一覧。
		private readonly List<Task> m_connections = new() ;
		// ループバックアドレスで HTTP 接続を受け付けるリスナー。
		private readonly TcpListener m_http ;
		// ループバックアドレスで HTTPS 接続を受け付けるリスナー。
		private readonly TcpListener m_https ;
		// コンテンツを公開するルートフォルダーのパス。
		private readonly string m_root ;
		// HTTPS 接続に使用する証明書。準備前は null。
		private X509Certificate2? m_certificate ;
		// HTTP と HTTPS の待ち受け処理全体の完了を表すタスク。
		private Task? m_acceptTask ;

		/// <summary>
		/// 公開ルートとポートを指定してサーバーを初期化します。
		/// </summary>
		/// <param name="root">コンテンツを公開するルートフォルダーのパス。</param>
		/// <param name="httpPort">HTTP の待ち受けポート番号。</param>
		/// <param name="httpsPort">HTTPS の待ち受けポート番号。</param>
		/// <param name="accessLog">アクセスログの通知先。通知しない場合は null。</param>
		public LocalHttpServer( string root, int httpPort, int httpsPort, Action<AccessLogEntry>? accessLog = null )
		{
			m_root = root ;
			m_accessLog = accessLog ;
			m_http = new TcpListener( IPAddress.Loopback, httpPort ) ;
			m_https = new TcpListener( IPAddress.Loopback, httpsPort ) ;
		}

		/// <summary>
		/// 証明書を準備し、HTTP と HTTPS の待ち受けを開始します。
		/// </summary>
		public void Start()
		{
			try
			{
				m_certificate = LoadOrCreateCertificate() ;
				m_http.Start() ;
				m_https.Start() ;
				m_acceptTask = Task.WhenAll( AcceptAsync( m_http, false ), AcceptAsync( m_https, true ) ) ;
			}
			catch
			{
				m_http.Stop() ;
				m_https.Stop() ;
				m_certificate?.Dispose() ;
				m_shutdown.Dispose() ;
				throw ;
			}
		}

		/// <summary>
		/// 待ち受けと接続処理を停止し、証明書などのリソースを解放します。
		/// </summary>
		/// <returns>すべての待ち受けと接続処理の終了を表すタスク。</returns>
		public async Task StopAsync()
		{
			m_shutdown.Cancel() ;
			m_http.Stop() ;
			m_https.Stop() ;
			try
			{
				if( m_acceptTask != null )
				{
					await m_acceptTask ;
				}
				await Task.WhenAll( m_connections ) ;
			}
			finally
			{
				m_certificate?.Dispose() ;
				m_shutdown.Dispose() ;
			}
		}

		// 停止要求まで接続を受け付け、処理中の接続タスクを管理する。
		private async Task AcceptAsync( TcpListener listener, bool useTls )
		{
			try
			{
				while( !m_shutdown.IsCancellationRequested )
				{
					var client = await listener.AcceptTcpClientAsync( m_shutdown.Token ) ;
					m_connections.RemoveAll( task => task.IsCompleted ) ;
					m_connections.Add( HandleClientAsync( client, useTls, m_certificate!, m_root, m_shutdown.Token ) ) ;
				}
			}
			catch( OperationCanceledException ) when( m_shutdown.IsCancellationRequested )
			{
				// 停止操作による待ち受け終了。
			}
			catch( SocketException ) when( m_shutdown.IsCancellationRequested )
			{
				// 停止時にリスナーが閉じられた。
			}
		}

		// 接続の暗号化と要求の検証を行い、ファイル応答とアクセスログを生成する。
		private async Task HandleClientAsync
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
					if( requestLine[ 0 ] is "GET" or "POST" )
					{
						System.Diagnostics.Trace.WriteLine( $"要求: {requestLine[ 0 ]} {rawPath}" ) ;
					}

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

					var relativeFile = Path.GetRelativePath( rootFullPath, filePath ) ;
					var currentPath = rootFullPath ;
					foreach( var segment in relativeFile.Split( Path.DirectorySeparatorChar ) )
					{
						currentPath = Path.Combine( currentPath, segment ) ;
						if( segment.Contains( ':' ) || ( Path.Exists( currentPath ) &&
							( File.GetAttributes( currentPath ) & FileAttributes.ReparsePoint ) != 0 ) )
						{
							await RespondAsync( stream, 403, "Forbidden", "アクセスできません。", "text/plain; charset=utf-8", headOnly, cancellationToken ) ;
							return ;
						}
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
						m_accessLog?.Invoke( new AccessLogEntry( method, requestPath, timestamp, responseStatus, error ) ) ;
					}
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
				"LHTTPD", "GUI"
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


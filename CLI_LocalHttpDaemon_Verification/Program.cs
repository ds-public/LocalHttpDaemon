using System.Diagnostics ;
using System.Net ;
using System.Net.Sockets ;
using System.Text.Json ;
using System.Text.RegularExpressions ;

namespace LHTTPD
{
	internal static class Verification
	{
		public static async Task<int> Main( string[] args )
		{
			// 引数で配布用EXEを指定すると、同じ検証を単独実行版に適用できる。
			var executable = args.Length > 0 ? Path.GetFullPath( args[ 0 ] ) :
				Path.Combine( AppContext.BaseDirectory, "CLI_LocalHttpDaemon.exe" ) ;
			var root = Path.Combine( AppContext.BaseDirectory, "LHTTPD-CLI-" + Guid.NewGuid() ) ;
			Directory.CreateDirectory( root ) ;
			// 配置先の設定や配布物を変更しないよう、実行環境を分離する。
			var application = Path.Combine( root, "application" ) ;
			Directory.CreateDirectory( application ) ;
			foreach( var file in Directory.GetFiles( Path.GetDirectoryName( executable )! ) )
			{
				if( Path.GetFileName( file ) != "settings.json" )
				{
					File.Copy( file, Path.Combine( application, Path.GetFileName( file ) ) ) ;
				}
			}
			executable = Path.Combine( application, Path.GetFileName( executable ) ) ;
			try
			{
				using( var help = StartProcess( executable, Array.Empty<string>() ) )
				{
					var helpOutput = help.StandardOutput.ReadToEndAsync() ;
					await help.WaitForExitAsync().WaitAsync( TimeSpan.FromSeconds( 10 ) ) ;
					Check( help.ExitCode == 0 && ( await helpOutput ).Contains( "--root" ), "ルート未指定でヘルプ表示・終了" ) ;
					Check( !File.Exists( Path.Combine( application, "settings.json" ) ), "ヘルプでは設定を保存しない" ) ;
				}
				Check( await Program.Main( new[] { "--help" } ) == 0, "ヘルプの終了コード" ) ;
				Check( await Program.Main( new[] { "--port", "80/80" } ) == 2, "同一ポートの拒否" ) ;
				await File.WriteAllTextAsync( Path.Combine( root, "index.html" ), "CLI test 日本語" ) ;
				var httpPort = FreePort() ;
				var httpsPort = FreePort() ;
				while( httpsPort == httpPort )
				{
					httpsPort = FreePort() ;
				}
				var startInfo = new ProcessStartInfo( executable )
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				} ;
				foreach( var argument in new[] { "--root", root, "--port", $"{httpPort}/{httpsPort}" } )
				{
					startInfo.ArgumentList.Add( argument ) ;
				}
				using var process = Process.Start( startInfo ) ?? throw new Exception( "CLIを起動できません。" ) ;
				var output = process.StandardOutput.ReadToEndAsync() ;
				var error = process.StandardError.ReadToEndAsync() ;
				try
				{
					using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = ( _, _, _, _ ) => true } ;
					using var client = new HttpClient( handler ) { Timeout = TimeSpan.FromSeconds( 5 ) } ;
					using var startupTimeout = new CancellationTokenSource( TimeSpan.FromSeconds( 30 ) ) ;
					while( true )
					{
						if( process.HasExited )
						{
							throw new Exception( "CLIが起動中に終了しました: " + await error ) ;
						}
						try
						{
							using var response = await client.GetAsync( $"http://127.0.0.1:{httpPort}/", startupTimeout.Token ) ;
							response.EnsureSuccessStatusCode() ;
							break ;
						}
						catch( HttpRequestException )
						{
							await Task.Delay( 100, startupTimeout.Token ) ;
						}
					}
					foreach( var address in new[] { $"http://127.0.0.1:{httpPort}", $"https://localhost:{httpsPort}" } )
					{
						Check( await client.GetStringAsync( address + "/" ) == "CLI test 日本語", "GET " + address ) ;
						using var malformed = new TcpClient() ;
						if( address.StartsWith( "http:" ) )
						{
							await malformed.ConnectAsync( IPAddress.Loopback, httpPort ) ;
							await malformed.GetStream().WriteAsync( "BAD\r\n\r\n"u8.ToArray() ) ;
							var responseBuffer = new byte[ 2048 ] ;
							Check( await malformed.GetStream().ReadAsync( responseBuffer ) > 0, "不正リクエストへの応答" ) ;
						}
						using var head = await client.SendAsync( new HttpRequestMessage( HttpMethod.Head, address + "/" ) ) ;
						Check( head.StatusCode == HttpStatusCode.OK && ( await head.Content.ReadAsByteArrayAsync() ).Length == 0, "HEAD" ) ;
						using var missing = await client.GetAsync( address + "/missing" ) ;
						Check( missing.StatusCode == HttpStatusCode.NotFound, "404" ) ;
						using var post = await client.PostAsync( address + "/", null ) ;
						Check( post.StatusCode == HttpStatusCode.MethodNotAllowed, "405" ) ;
					}
				}
				finally
				{
					// 検証で起動した子プロセスだけを終了し、失敗時にも残さない。
					if( !process.HasExited )
					{
						process.Kill( true ) ;
					}
					await process.WaitForExitAsync() ;
					await Task.WhenAll( output, error ) ;
				}
				var logs = await output ;
				foreach( var expected in new[] { @"\[GET\] \d{4}/\d{2}/\d{2} \d{2}:\d{2} 200 /", @"\[HEAD\].* 200 /", @"\[GET\].* 404 missing", @"\[POST\].* 405 /", @"\[ERROR\].* 400 /" } )
				{
					Check( Regex.IsMatch( logs, expected ), "ログ形式: " + expected ) ;
				}
				var settingsPath = Path.Combine( application, "settings.json" ) ;
				var saved = JsonSerializer.Deserialize<ServerSettings>( await File.ReadAllTextAsync( settingsPath ) )! ;
				Check( saved.RootPath == root && saved.HttpPort == httpPort && saved.HttpsPort == httpsPort, "GUI互換の設定保存" ) ;
				var writeTime = File.GetLastWriteTimeUtc( settingsPath ) ;
				await VerifyRestartAsync( executable, Array.Empty<string>(), httpPort, httpsPort ) ;
				Check( File.GetLastWriteTimeUtc( settingsPath ) == writeTime, "保存済み設定から起動・同一設定は書き換えない" ) ;
				foreach( var suffix in new[] { "", "/" } )
				{
					var newHttpPort = FreePort() ;
					while( newHttpPort == httpsPort )
					{
						newHttpPort = FreePort() ;
					}
					await VerifyRestartAsync( executable, new[] { "--port", newHttpPort + suffix }, newHttpPort, httpsPort ) ;
					saved = JsonSerializer.Deserialize<ServerSettings>( await File.ReadAllTextAsync( settingsPath ) )! ;
					Check( saved.HttpPort == newHttpPort && saved.HttpsPort == httpsPort, "HTTPのみ更新・HTTPS維持: " + suffix ) ;
				}
				var otherRoot = Path.Combine( root, "other" ) ;
				Directory.CreateDirectory( otherRoot ) ;
				await File.WriteAllTextAsync( Path.Combine( otherRoot, "index.html" ), "CLI test 日本語" ) ;
				await VerifyRestartAsync( executable, new[] { "--root", otherRoot }, saved.HttpPort, httpsPort ) ;
				saved = JsonSerializer.Deserialize<ServerSettings>( await File.ReadAllTextAsync( settingsPath ) )! ;
				Check( saved.RootPath == otherRoot, "引数のルートを優先して保存" ) ;
				foreach( var port in new[] { httpPort, httpsPort } )
				{
					using var listener = new TcpListener( IPAddress.Loopback, port ) ;
					listener.Start() ;
					Check( true, "プロセス終了後のポート解放" ) ;
				}
				return 0 ;
			}
			catch( Exception ex )
			{
				Console.Error.WriteLine( ex ) ;
				return 1 ;
			}
			finally
			{
				// Windowsでは終了直後のEXEが短時間ロックされる場合がある。
				for( var attempt = 0 ; ; attempt ++ )
				{
					try
					{
						Directory.Delete( root, true ) ;
						break ;
					}
					catch( Exception ex ) when( attempt < 20 && ex is IOException or UnauthorizedAccessException )
					{
						await Task.Delay( 250 ) ;
					}
				}
			}
		}


		private static Process StartProcess( string executable, string[] arguments )
		{
			var info = new ProcessStartInfo( executable )
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			} ;
			foreach( var argument in arguments )
			{
				info.ArgumentList.Add( argument ) ;
			}
			return Process.Start( info ) ?? throw new Exception( "CLIを起動できません。" ) ;
		}

		private static async Task VerifyRestartAsync( string executable, string[] arguments, int httpPort, int httpsPort )
		{
			using var process = StartProcess( executable, arguments ) ;
			var output = process.StandardOutput.ReadToEndAsync() ;
			var error = process.StandardError.ReadToEndAsync() ;
			try
			{
				using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 15 ) ) ;
				using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = ( _, _, _, _ ) => true } ;
				using var client = new HttpClient( handler ) ;
				while( true )
				{
					if( process.HasExited )
					{
						throw new Exception( "再起動失敗: " + await error ) ;
					}
					try
					{
						Check( await client.GetStringAsync( $"http://localhost:{httpPort}/", timeout.Token ) == "CLI test 日本語", "再起動HTTP" ) ;
						break ;
					}
					catch( HttpRequestException )
					{
						await Task.Delay( 100, timeout.Token ) ;
					}
				}
				Check( await client.GetStringAsync( $"https://localhost:{httpsPort}/", timeout.Token ) == "CLI test 日本語", "再起動HTTPS" ) ;
			}
			finally
			{
				if( !process.HasExited )
				{
					process.Kill( true ) ;
				}
				await process.WaitForExitAsync() ;
				await Task.WhenAll( output, error ) ;
			}
		}

		private static int FreePort()
		{
			using var listener = new TcpListener( IPAddress.Loopback, 0 ) ;
			listener.Start() ;
			return ( (IPEndPoint)listener.LocalEndpoint ).Port ;
		}

		private static void Check( bool condition, string name )
		{
			if( !condition )
			{
				throw new Exception( name ) ;
			}
			Console.WriteLine( "PASS: " + name ) ;
		}
	}
}

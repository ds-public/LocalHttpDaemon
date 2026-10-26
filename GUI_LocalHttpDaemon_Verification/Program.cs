using System.IO ;
using System.Net ;
using System.Net.Http ;
using System.Net.Sockets ;
using System.Windows ;
using System.Windows.Controls ;
using LHTTPD ;

namespace LHTTPD
{
	internal static class Verification
	{
		[STAThread]
		public static void Main()
		{
			var settingsBackup = File.Exists( ServerSettings.SettingsPath ) ? File.ReadAllBytes( ServerSettings.SettingsPath ) : null ;
			File.Delete( ServerSettings.SettingsPath ) ;
			var app = new Application() ;
			var window = new MainWindow() ;
			window.Loaded += async ( _, _ ) =>
			{
				var root = Path.Combine( Path.GetTempPath(), "LHTTPD-GUI-" + Guid.NewGuid() ) ;
				Directory.CreateDirectory( root ) ;
				try
				{
					await File.WriteAllTextAsync( Path.Combine( root, "index.html" ), "GUI test 日本語" ) ;
					var httpPort = FreePort() ;
					var httpsPort = FreePort() ;
					while( httpsPort == httpPort )
					{
						httpsPort = FreePort() ;
					}
					var rootBox = (TextBox)window.FindName( "RootPath" ) ;
					var httpBox = (TextBox)window.FindName( "HttpPort" ) ;
					var httpsBox = (TextBox)window.FindName( "HttpsPort" ) ;
					var button = (Button)window.FindName( "RunButton" ) ;
					Check( httpBox.Text == "80" && httpsBox.Text == "443", "既定値" ) ;
					rootBox.Text = root ;
					httpBox.Text = httpPort.ToString() ;
					httpsBox.Text = httpsPort.ToString() ;
					button.RaiseEvent( new RoutedEventArgs( Button.ClickEvent ) ) ;
					Check( (string)button.Content == "停止" && !rootBox.IsEnabled && !httpBox.IsEnabled && !httpsBox.IsEnabled, "開始時のUI" ) ;
					var savedSettings = ServerSettings.Load() ;
					Check( savedSettings.RootPath == root && savedSettings.HttpPort == httpPort && savedSettings.HttpsPort == httpsPort,
						"実行時にEXE配置先のsettings.jsonへ保存" ) ;
					var restoredWindow = new MainWindow() ;
					Check( ( (TextBox)restoredWindow.FindName( "RootPath" ) ).Text == root &&
						( (TextBox)restoredWindow.FindName( "HttpPort" ) ).Text == httpPort.ToString() &&
						( (TextBox)restoredWindow.FindName( "HttpsPort" ) ).Text == httpsPort.ToString() &&
						(string)( (Button)restoredWindow.FindName( "RunButton" ) ).Content == "実行", "次回ウィンドウ生成時の3項目復元・自動開始なし" ) ;
					restoredWindow.Close() ;
					var settingsJson = File.ReadAllText( ServerSettings.SettingsPath ) ;
					File.WriteAllText( ServerSettings.SettingsPath, "{broken" ) ;
					try
					{
						ServerSettings.Load() ;
						throw new Exception( "不正JSONを検出できません。" ) ;
					}
					catch( System.Text.Json.JsonException )
					{
						Check( true, "破損JSONの検出" ) ;
					}
					File.WriteAllText( ServerSettings.SettingsPath, settingsJson ) ;
					using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = ( _, _, _, _ ) => true } ;
					using var client = new HttpClient( handler ) ;
					foreach( var address in new[] { $"http://127.0.0.1:{httpPort}", $"https://localhost:{httpsPort}" } )
					{
						Check( await client.GetStringAsync( address + "/" ) == "GUI test 日本語", "GET " + address ) ;
						using var head = await client.SendAsync( new HttpRequestMessage( HttpMethod.Head, address + "/" ) ) ;
						Check( head.StatusCode == HttpStatusCode.OK && ( await head.Content.ReadAsByteArrayAsync() ).Length == 0, "HEAD" ) ;
						Check( ( await client.GetAsync( address + "/missing" ) ).StatusCode == HttpStatusCode.NotFound, "404" ) ;
						Check( ( await client.PostAsync( address + "/", new StringContent( "test" ) ) ).StatusCode == HttpStatusCode.MethodNotAllowed, "405" ) ;
					}
					var toggle = (Button)window.FindName( "LogToggleButton" ) ;
					var log = (ListBox)window.FindName( "AccessLog" ) ;
					Check( log.Visibility == Visibility.Collapsed && log.Items.Count == 8, "非表示中もHTTP/HTTPSの全メソッドを記録" ) ;
					Check( log.Background == System.Windows.Media.Brushes.Black, "ログ背景は黒" ) ;
					foreach( var offset in new[] { 0, 4 } )
					{
						Check( ( (AccessLogEntry)log.Items[ offset ] ).Foreground == System.Windows.Media.Brushes.White &&
							( (AccessLogEntry)log.Items[ offset + 1 ] ).Foreground == System.Windows.Media.Brushes.Yellow &&
							( (AccessLogEntry)log.Items[ offset + 2 ] ).Foreground == System.Windows.Media.Brushes.Red &&
							( (AccessLogEntry)log.Items[ offset + 3 ] ).Foreground == System.Windows.Media.Brushes.Yellow,
							"GET白・HEAD黄・404赤・POST405黄" ) ;
					}
					Check( new AccessLogEntry( "GET", "/", DateTime.Now, 302 ).Foreground == System.Windows.Media.Brushes.Yellow,
						"3xxの色分類" ) ;
					await File.WriteAllTextAsync( Path.Combine( root, "locked.txt" ), "locked" ) ;
					using( var lockedFile = new FileStream( Path.Combine( root, "locked.txt" ), FileMode.Open, FileAccess.ReadWrite, FileShare.None ) )
					{
						try
						{
							using var response = await client.GetAsync( $"http://127.0.0.1:{httpPort}/locked.txt" ) ;
						}
						catch( HttpRequestException )
						{
							// ファイル読み込み失敗時は接続が終了する。
						}
					}
					Check( ( (AccessLogEntry)log.Items[ log.Items.Count - 1 ] ).Foreground == System.Windows.Media.Brushes.Red &&
						( (AccessLogEntry)log.Items[ log.Items.Count - 1 ] ).Text.Contains( "通信・ファイル処理エラー" ), "処理エラーを赤で記録" ) ;
					var initialHeight = window.ActualHeight ;
					var initialTop = window.Top ;
					toggle.RaiseEvent( new RoutedEventArgs( Button.ClickEvent ) ) ;
					window.UpdateLayout() ;
					await window.Dispatcher.InvokeAsync( () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle ) ;
					Check( log.IsVisible && (string)toggle.Content == "▲ログ隠蔽" &&
						Math.Abs( window.ActualHeight - initialHeight * 2 ) < 3 && window.Top == initialTop, "ログ表示で下方向に約2倍" ) ;
					for( var index = 0 ; index < 1001 ; index ++ )
					{
						using var response = await client.GetAsync( $"http://127.0.0.1:{httpPort}/log-{index}" ) ;
					}
					Check( log.Items.Count == 1000 && ( ( (AccessLogEntry)log.Items[ 0 ] ).Text ).EndsWith( " log-1" ) &&
						( ( (AccessLogEntry)log.Items[ 999 ] ).Text ).EndsWith( " log-1000" ), "1000件保持・古いログから削除" ) ;
					Check( System.Text.RegularExpressions.Regex.IsMatch( ( (AccessLogEntry)log.Items[ 0 ] ).Text,
						@"^\[GET\] \d{4}/\d{2}/\d{2} \d{2}:\d{2} 404 log-1$" ), "ログの表示書式" ) ;
					toggle.RaiseEvent( new RoutedEventArgs( Button.ClickEvent ) ) ;
					window.UpdateLayout() ;
					await window.Dispatcher.InvokeAsync( () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle ) ;
					Check( log.Visibility == Visibility.Collapsed && (string)toggle.Content == "▼ログ表示" &&
						Math.Abs( window.ActualHeight - initialHeight ) < 3 && log.Items.Count == 1000, "ログ隠蔽で初期サイズに戻り履歴保持" ) ;
					toggle.RaiseEvent( new RoutedEventArgs( Button.ClickEvent ) ) ;
					window.UpdateLayout() ;
					await window.Dispatcher.InvokeAsync( () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle ) ;
					Check( Math.Abs( window.ActualHeight - initialHeight * 2 ) < 3, "繰り返し表示のサイズ維持" ) ;
					button.RaiseEvent( new RoutedEventArgs( Button.ClickEvent ) ) ;
					while( !button.IsEnabled )
					{
						await Task.Delay( 20 ) ;
					}
					Check( (string)button.Content == "実行" && rootBox.IsEnabled && httpBox.IsEnabled && httpsBox.IsEnabled, "停止時のUI" ) ;
					using( var probe = new TcpListener( IPAddress.Loopback, httpPort ) )
					{
						probe.Start() ;
						Check( true, "停止後のポート解放" ) ;
					}
					using( var occupied = new TcpListener( IPAddress.Loopback, httpsPort ) )
					{
						occupied.Start() ;
						try
						{
							new LocalHttpServer( root, httpPort, httpsPort ).Start() ;
							throw new Exception( "ポート競合を検出しませんでした。" ) ;
						}
						catch( SocketException )
						{
							using var probe = new TcpListener( IPAddress.Loopback, httpPort ) ;
							probe.Start() ;
							Check( true, "開始失敗時のロールバック" ) ;
						}
					}
					button.RaiseEvent( new RoutedEventArgs( Button.ClickEvent ) ) ;
					window.Close() ;
					using var finalProbe = new TcpListener( IPAddress.Loopback, httpPort ) ;
					finalProbe.Start() ;
					Check( true, "再実行とウィンドウ終了時のポート解放" ) ;
				}
				catch( Exception ex )
				{
					Console.Error.WriteLine( ex ) ;
					Environment.ExitCode = 1 ;
				}
				finally
				{
					Directory.Delete( root, true ) ;
					if( settingsBackup == null )
					{
						File.Delete( ServerSettings.SettingsPath ) ;
					}
					else
					{
						File.WriteAllBytes( ServerSettings.SettingsPath, settingsBackup ) ;
					}
					app.Shutdown() ;
				}
			} ;
			app.Run( window ) ;
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

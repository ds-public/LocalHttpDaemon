using System.Collections.ObjectModel ;
using System.ComponentModel ;
using System.Globalization ;
using System.IO ;
using System.Windows ;
using System.Windows.Controls ;
using System.Windows.Input ;
using Microsoft.Win32 ;

namespace LHTTPD
{
	public partial class MainWindow : Window
	{
		private const int MaximumLogEntries = 1000 ;
		private readonly ObservableCollection<AccessLogEntry> m_accessLogs = new() ;
		private LocalHttpServer? m_server ;
		private bool m_closing ;
		private bool m_busy ;

		public MainWindow()
		{
			InitializeComponent() ;
			AccessLog.ItemsSource = m_accessLogs ;
			try
			{
				var settings = ServerSettings.Load() ;
				RootPath.Text = settings.RootPath ;
				HttpPort.Text = settings.HttpPort.ToString( CultureInfo.InvariantCulture ) ;
				HttpsPort.Text = settings.HttpsPort.ToString( CultureInfo.InvariantCulture ) ;
			}
			catch( Exception ex ) when( ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException )
			{
				Loaded += ( _, _ ) => ShowError( $"settings.json を読み込めません。既定値で起動します。\n{ex.Message}" ) ;
			}
		}

		private void LogToggle_Click( object sender, RoutedEventArgs e )
		{
			if( AccessLog.Visibility == Visibility.Visible )
			{
				AccessLog.Visibility = Visibility.Collapsed ;
				LogToggleButton.Content = "▼ログ表示" ;
			}
			else
			{
				// 追加領域と余白の合計を初期ウィンドウ高に合わせる。
				AccessLog.Height = Math.Max( 100, ActualHeight - AccessLog.Margin.Top ) ;
				AccessLog.Visibility = Visibility.Visible ;
				LogToggleButton.Content = "▲ログ隠蔽" ;
			}
		}

		private void RecordAccess( AccessLogEntry entry )
		{
			void AppendEntry()
			{
				if( m_accessLogs.Count >= MaximumLogEntries )
				{
					m_accessLogs.RemoveAt( 0 ) ;
				}
				m_accessLogs.Add( entry ) ;
			}
			if( Dispatcher.CheckAccess() )
			{
				AppendEntry() ;
			}
			else
			{
				_ = Dispatcher.InvokeAsync( AppendEntry ) ;
			}
		}

		private void Browse_Click( object sender, RoutedEventArgs e )
		{
			var dialog = new OpenFolderDialog { Title = "ルートフォルダを選択", Multiselect = false } ;
			if( dialog.ShowDialog( this ) == true )
			{
				RootPath.Text = dialog.FolderName ;
			}
		}

		private static bool IsDigits( string text )
		{
			return text.All( character => character is >= '0' and <= '9' ) ;
		}

		private void Port_PreviewTextInput( object sender, TextCompositionEventArgs e )
		{
			e.Handled = !IsDigits( e.Text ) ;
		}

		private void Port_Pasting( object sender, DataObjectPastingEventArgs e )
		{
			if( !e.DataObject.GetDataPresent( DataFormats.UnicodeText ) ||
				e.DataObject.GetData( DataFormats.UnicodeText ) is not string text || !IsDigits( text ) )
			{
				e.CancelCommand() ;
			}
		}

		private void ShowError( string message, Control? target = null )
		{
			MessageBox.Show( this, message, "入力・実行エラー", MessageBoxButton.OK, MessageBoxImage.Error ) ;
			target?.Focus() ;
		}

		private bool Validate( out string root, out int httpPort, out int httpsPort )
		{
			root = RootPath.Text.Trim() ;
			httpPort = 0 ;
			httpsPort = 0 ;
			if( string.IsNullOrWhiteSpace( root ) )
			{
				ShowError( "ルートフォルダの絶対パスを入力してください。", RootPath ) ;
				return false ;
			}
			if( !Path.IsPathFullyQualified( root ) || !Directory.Exists( root ) )
			{
				ShowError( "ルートフォルダが存在しません。存在するフォルダの絶対パスを指定してください。", RootPath ) ;
				return false ;
			}
			root = Path.GetFullPath( root ) ;
			if( !IsDigits( HttpPort.Text ) || !int.TryParse( HttpPort.Text, out httpPort ) || httpPort is < 1 or > 65535 )
			{
				ShowError( "HTTPのポート番号を1～65535の半角数字で入力してください。", HttpPort ) ;
				return false ;
			}
			if( !IsDigits( HttpsPort.Text ) || !int.TryParse( HttpsPort.Text, out httpsPort ) || httpsPort is < 1 or > 65535 )
			{
				ShowError( "HTTPSのポート番号を1～65535の半角数字で入力してください。", HttpsPort ) ;
				return false ;
			}
			if( httpPort == httpsPort )
			{
				ShowError( "HTTPとHTTPSには異なるポート番号を指定してください。", HttpsPort ) ;
				return false ;
			}
			return true ;
		}

		private async void Run_ClickAsync( object sender, RoutedEventArgs e )
		{
			if( m_busy )
			{
				return ;
			}
			m_busy = true ;
			RunButton.IsEnabled = false ;
			try
			{
				if( m_server != null )
				{
					await StopAsync() ;
				}
				else if( Validate( out var root, out var httpPort, out var httpsPort ) )
				{
					try
					{
						new ServerSettings { RootPath = root, HttpPort = httpPort, HttpsPort = httpsPort }.Save() ;
					}
					catch( Exception ex ) when( ex is IOException or UnauthorizedAccessException )
					{
						ShowError( $"EXEと同じフォルダに settings.json を保存できません。フォルダの書き込み権限を確認してください。\n{ex.Message}" ) ;
						return ;
					}
					var server = new LocalHttpServer( root, httpPort, httpsPort, RecordAccess ) ;
					server.Start() ;
					m_server = server ;
					SettingsPanel.IsEnabled = false ;
					RunButton.Content = "停止" ;
				}
			}
			catch( Exception ex )
			{
				ShowError( $"サーバーの操作に失敗しました。ポートの使用状況やアクセス権を確認してください。\n{ex.Message}" ) ;
			}
			finally
			{
				m_busy = false ;
				RunButton.IsEnabled = true ;
			}
		}

		private async Task StopAsync()
		{
			var server = m_server ;
			m_server = null ;
			try
			{
				if( server != null )
				{
					await server.StopAsync() ;
				}
			}
			finally
			{
				SettingsPanel.IsEnabled = true ;
				RunButton.Content = "実行" ;
			}
		}

		private async void Window_ClosingAsync( object? sender, CancelEventArgs e )
		{
			if( m_closing || ( m_server == null && !m_busy ) )
			{
				return ;
			}
			e.Cancel = true ;
			if( m_busy )
			{
				return ;
			}
			m_busy = true ;
			IsEnabled = false ;
			try
			{
				await StopAsync() ;
			}
			catch( Exception ex )
			{
				ShowError( $"停止処理でエラーが発生しました。\n{ex.Message}" ) ;
			}
			finally
			{
				m_closing = true ;
				Close() ;
			}
		}
	}
}

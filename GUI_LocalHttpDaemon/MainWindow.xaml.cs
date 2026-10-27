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

	/// <summary>
	/// 公開設定の入力、サーバーの開始・停止、アクセスログ表示を行うウィンドウです。
	/// </summary>
	public partial class MainWindow : Window
	{

		// 画面に保持するアクセスログの最大件数。
		private const int MaximumLogEntries = 1000 ;

		// 画面に表示するアクセスログのコレクション。
		private readonly ObservableCollection<AccessLogEntry> m_accessLogs = new() ;

		// 実行中のサーバー。停止中は null。
		private LocalHttpServer? m_server ;

		// サーバー停止後のウィンドウ終了を許可する状態。
		private bool m_closing ;

		// 開始・停止処理の多重実行を防ぐ操作中フラグ。
		private bool m_busy ;

		/// <summary>
		/// 画面を初期化し、保存済みの公開設定とログ表示を準備します。
		/// </summary>
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

		// アクセスログの表示と非表示を切り替える。
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

		// UI スレッドでアクセスログを追加し、保持件数の上限を守る。
		private void RecordAccess( AccessLogEntry entry )
		{

			// 保持件数の上限を超える古いログを削除して新しいログを追加する。
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

		// フォルダー選択ダイアログで公開ルートを設定する。
		private void Browse_Click( object sender, RoutedEventArgs e )
		{
			var dialog = new OpenFolderDialog { Title = "ルートフォルダを選択", Multiselect = false } ;
			if( dialog.ShowDialog( this ) == true )
			{
				RootPath.Text = dialog.FolderName ;
			}
		}

		// 入力が半角数字だけで構成されているかを判定する。
		private static bool IsDigits( string text )
		{
			return text.All( character => character is >= '0' and <= '9' ) ;
		}

		// ポート入力欄への半角数字以外の入力を抑止する。
		private void Port_PreviewTextInput( object sender, TextCompositionEventArgs e )
		{
			e.Handled = !IsDigits( e.Text ) ;
		}

		// ポート入力欄への半角数字以外の貼り付けを取り消す。
		private void Port_Pasting( object sender, DataObjectPastingEventArgs e )
		{
			if( !e.DataObject.GetDataPresent( DataFormats.UnicodeText ) ||
				e.DataObject.GetData( DataFormats.UnicodeText ) is not string text || !IsDigits( text ) )
			{
				e.CancelCommand() ;
			}
		}

		// エラーを表示し、必要に応じて修正対象の入力欄へフォーカスを移す。
		private void ShowError( string message, Control? target = null )
		{
			MessageBox.Show( this, message, "入力・実行エラー", MessageBoxButton.OK, MessageBoxImage.Error ) ;
			target?.Focus() ;
		}

		// 公開ルートとポートを検証し、有効な設定値を出力する。
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

		// 設定を保存してサーバーを開始するか、実行中のサーバーを停止する。
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

		// サーバーの停止を待機し、設定画面を操作可能に戻す。
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

		// 終了操作時にサーバーの停止を待ってウィンドウを閉じる。
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

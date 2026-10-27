using System.Globalization ;
using System.Windows.Media ;

namespace LHTTPD
{
	/// <summary>
	/// 画面に表示するアクセスログの本文と表示色を保持します。
	/// </summary>
	public sealed class AccessLogEntry
	{
		/// <summary>
		/// 要求日時と応答結果を含むログ本文を取得します。
		/// </summary>
		/// <value>制御文字を空白に置換した単一行のアクセスログ。</value>
		public string Text { get ; }

		/// <summary>
		/// 応答結果に応じたログの文字色を取得します。
		/// </summary>
		/// <value>エラーは赤、警告は黄、通常の応答は白のブラシ。</value>
		public Brush Foreground { get ; }

		/// <summary>
		/// 要求と応答結果からログ本文と表示色を生成します。
		/// </summary>
		/// <param name="method">要求の HTTP メソッド。</param>
		/// <param name="path">要求された URL パス。</param>
		/// <param name="timestamp">接続を受け付けたローカル日時。</param>
		/// <param name="statusCode">HTTP 応答ステータスコード。応答がない場合は 0。</param>
		/// <param name="error">処理失敗の説明。エラーがない場合は null。</param>
		public AccessLogEntry( string method, string path, DateTime timestamp, int statusCode, string? error = null )
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
			Text = $"[{SingleLine( method )}] {timestamp.ToString( "yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture )} {status} {displayPath}" ;
			if( error != null )
			{
				Text += $" — {SingleLine( error )}" ;
			}

			// POST等の未対応メソッド(405)は警告。404や処理失敗を優先する。
			Foreground = error != null || statusCode == 404 || statusCode >= 500 ? Brushes.Red :
				method != "GET" || statusCode is >= 300 and < 400 ? Brushes.Yellow :
				statusCode >= 400 ? Brushes.Red : Brushes.White ;
		}
	}
}

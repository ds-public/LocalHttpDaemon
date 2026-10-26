using System.Globalization ;
using System.Windows.Media ;

namespace LHTTPD
{
	public sealed class AccessLogEntry
	{
		public string Text { get ; }
		public Brush Foreground { get ; }

		public AccessLogEntry( string method, string path, DateTime timestamp, int statusCode, string? error = null )
		{
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

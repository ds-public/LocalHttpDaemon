using System.IO ;
using System.Text.Json ;

namespace LHTTPD
{
	/// <summary>
	/// 設定ファイルに保存する公開ルートと HTTP・HTTPS のポートを保持します。
	/// </summary>
	public sealed class ServerSettings
	{
		/// <summary>
		/// 公開ルートを取得または設定します。
		/// </summary>
		/// <value>公開ルートの絶対パス。未設定時は空文字列。</value>
		public string RootPath { get ; set ; } = "" ;

		/// <summary>
		/// HTTP の待ち受けポート番号を取得または設定します。
		/// </summary>
		/// <value>HTTP のポート番号。既定値は 80。</value>
		public int HttpPort { get ; set ; } = 80 ;

		/// <summary>
		/// HTTPS の待ち受けポート番号を取得または設定します。
		/// </summary>
		/// <value>HTTPS のポート番号。既定値は 443。</value>
		public int HttpsPort { get ; set ; } = 443 ;

		// 単一ファイル発行時の一時展開先ではなく、EXEの配置先を使用する。
		/// <summary>
		/// EXE の配置先にある設定ファイルのパスを取得します。
		/// </summary>
		/// <value>単一ファイル発行時も EXE と同じフォルダーにある settings.json のパス。</value>
		public static string SettingsPath => Path.Combine( AppContext.BaseDirectory, "settings.json" ) ;

		/// <summary>
		/// 設定ファイルを読み込み、設定値を検証します。
		/// </summary>
		/// <returns>読み込んだ設定。ファイルが存在しない場合は既定の設定。</returns>
		public static ServerSettings Load()
		{
			try
			{
				var settings = JsonSerializer.Deserialize<ServerSettings>( File.ReadAllText( SettingsPath ) ) ;
				if( settings == null || settings.RootPath == null ||
					settings.HttpPort is < 1 or > 65535 || settings.HttpsPort is < 1 or > 65535 ||
					settings.HttpPort == settings.HttpsPort )
				{
					throw new JsonException( "設定値が不正です。" ) ;
				}
				return settings ;
			}
			catch( FileNotFoundException )
			{
				return new ServerSettings() ;
			}
		}

		/// <summary>
		/// 現在の設定を一時ファイル経由で設定ファイルへ保存します。
		/// </summary>
		public void Save()
		{
			// 同一フォルダの一時ファイルを置換し、書き込み失敗で前回設定を壊さない。
			var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString( "N" ) + ".tmp" ;
			try
			{
				File.WriteAllText( temporaryPath, JsonSerializer.Serialize( this, new JsonSerializerOptions { WriteIndented = true } ) ) ;
				File.Move( temporaryPath, SettingsPath, true ) ;
			}
			finally
			{
				if( File.Exists( temporaryPath ) )
				{
					File.Delete( temporaryPath ) ;
				}
			}
		}
	}
}

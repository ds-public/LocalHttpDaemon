using System.IO ;
using System.Text.Json ;

namespace LHTTPD
{
	public sealed class ServerSettings
	{
		public string RootPath { get ; set ; } = "" ;
		public int HttpPort { get ; set ; } = 80 ;
		public int HttpsPort { get ; set ; } = 443 ;

		// 単一ファイル発行時の一時展開先ではなく、EXEの配置先を使用する。
		public static string SettingsPath => Path.Combine( AppContext.BaseDirectory, "settings.json" ) ;

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

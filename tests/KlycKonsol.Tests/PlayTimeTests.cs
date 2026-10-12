using KlycKonsol.Library;
using Xunit;

namespace KlycKonsol.Tests;

[Collection("Loc")]
public class PlayTimeTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(-5, "")]
    [InlineData(30, "1 dk'dan az")]
    [InlineData(60, "1 dk")]
    [InlineData(45 * 60, "45 dk")]
    [InlineData(3600, "1 sa")]
    [InlineData(3600 + 30 * 60, "1 sa 30 dk")]
    [InlineData(150 * 3600 + 20 * 60, "150 sa")]
    public void Format(long seconds, string expected) => Assert.Equal(expected, PlayTime.Format(seconds));
}

public class VdfTests
{
    const string LoginUsers = """
        "users"
        {
        	"76561198000000000"
        	{
        		"AccountName"		"oyuncu_bir"
        		"PersonaName"		"Oyuncu"
        		"Timestamp"		"1791744958"
        	}
        	"76561198100000000"
        	{
        		"AccountName"		"other"
        		"Timestamp"		"1700000000"
        	}
        }
        """;

    const string LocalConfig = """
        "UserLocalConfigStore"
        {
        	"Software"
        	{
        		"Valve"
        		{
        			"Steam"
        			{
        				"apps"
        				{
        					"730"
        					{
        						"LastPlayed"		"1715533703"
        						"Playtime"		"90"
        						"cloud"
        						{
        							"last_sync_state"		"synchronized"
        						}
        					}
        					"2835570"
        					{
        						"cloud"		{ "last_sync_state" "synchronized" }
        						"LastPlayed"		"1783177278"
        						"Playtime"		"313"
        					}
        					"7"
        					{
        						"cloud"		{ "x" "y" }
        					}
        				}
        			}
        		}
        	}
        }
        """;

    [Fact]
    public void Parse_NestedBlocksAndValues()
    {
        var root = Vdf.Parse(LoginUsers);
        var users = root["users"]!;
        Assert.Equal(2, users.Children.Count);
        Assert.Equal("oyuncu_bir", users["76561198000000000"]!.Str("AccountName"));
        Assert.Equal(1791744958, users["76561198000000000"]!.Long("Timestamp"));
    }

    [Fact]
    public void MostRecentUser_PicksNewestTimestampAndConvertsToAccountId()
    {
        Assert.Equal("39734272", SteamLocal.MostRecentUserIdFrom(LoginUsers));
    }

    [Fact]
    public void MostRecentUser_FlagWinsOverTimestamp()
    {
        var vdf = LoginUsers.Replace("\"AccountName\"\t\t\"other\"", "\"AccountName\"\t\t\"other\"\n\t\t\"MostRecent\"\t\t\"1\"");
        Assert.Equal("139734272", SteamLocal.MostRecentUserIdFrom(vdf));
    }

    [Fact]
    public void ReadUsage_ParsesPlaytimeAndLastPlayedIgnoringEmptyApps()
    {
        var usage = SteamLocal.ReadUsage(LocalConfig);
        Assert.Equal(2, usage.Count);
        var cs = usage.Single(a => a.AppId == "730");
        Assert.Equal(90, cs.PlaytimeMinutes);
        Assert.Equal(1715533703, cs.LastPlayed);
        Assert.Equal(313, usage.Single(a => a.AppId == "2835570").PlaytimeMinutes);
    }

    [Fact]
    public void Parse_HandlesCommentsAndEscapes()
    {
        var root = Vdf.Parse("// yorum\n\"a\" { \"k\" \"x\\\\y\" } // son");
        Assert.Equal("x\\y", root["a"]!.Str("k"));
    }
}

public class GameSessionTests
{
    const string TrOutput = """
        Existing Power Schemes (* Active)
        -----------------------------------
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Dengeli) *
        Power Scheme GUID: 3f405fd2-d064-471a-aee8-6b514f2f17cb  (Nihai Performans)
        """;

    [Fact]
    public void ParseSchemes_ReadsGuidNameAndActiveFlag()
    {
        var s = KlycKonsol.Input.GameSession.ParseSchemes(TrOutput);
        Assert.Equal(2, s.Count);
        Assert.True(s[0].Active);
        Assert.Equal("Dengeli", s[0].Name);
        Assert.False(s[1].Active);
    }

    [Fact]
    public void PickTarget_PrefersHighThenUltimate_NeverBalanced()
    {
        var s = KlycKonsol.Input.GameSession.ParseSchemes(TrOutput);
        Assert.Equal("Nihai Performans", KlycKonsol.Input.GameSession.PickTarget(s)!.Name);

        var withHigh = KlycKonsol.Input.GameSession.ParseSchemes(TrOutput + "\nPower Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Yüksek performans)");
        Assert.Equal("Yüksek performans", KlycKonsol.Input.GameSession.PickTarget(withHigh)!.Name);

        var onlyBalanced = KlycKonsol.Input.GameSession.ParseSchemes("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *");
        Assert.Null(KlycKonsol.Input.GameSession.PickTarget(onlyBalanced));
    }
}

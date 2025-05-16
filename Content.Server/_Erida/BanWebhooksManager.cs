using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.Discord;
using Content.Shared.CCVar;
using Content.DeadSpace.Interfaces.Server;
using Content.Server.Database;
using Content.Server.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._Erida;

public sealed class DiscordBansSystem : IServerBanWebhooksManager

{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

    private readonly HttpClient _httpClient = new();

    public async Task SendBan(string? target,
        string? admin,
        uint? minutes,
        string reason,
        DateTimeOffset? expires,
        string? job,
        int color,
        string ban_type,
        int? roundId)
    {
        var webhookUrl = _cfg.GetCVar(CCVars.DiscordBansWebhook);
        var serverName = _cfg.GetCVar(CCVars.GameHostName);

        serverName = serverName[..Math.Min(serverName.Length, 1500)];
        if (string.IsNullOrEmpty(webhookUrl))
            return;

        var payload = new WebhookPayload
        {
            Username = serverName,
            AvatarUrl = "",
            Embeds = new List<WebhookEmbed>
            {
                new WebhookEmbed
                {
                    Color = color,
                    Description = GenerateBanDescription(target,
                        admin,
                        Convert.ToUInt32(minutes),
                        reason,
                        expires,
                        job,
                        color,
                        ban_type),
                    Footer = new WebhookEmbedFooter
                    {
                        Text = $"(раунд {roundId})"
                    }
                }
            }
        };

        await _httpClient.PostAsync($"{webhookUrl}?wait=true",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
    }

    public async Task SendDepartmentBan(string target,
        ICommonSession? admin,
        string department,
        string reason,
        uint minutes)
    {
        var webhookUrl = _cfg.GetCVar(CCVars.DiscordBansWebhook);
        var serverName = _cfg.GetCVar(CCVars.GameHostName);
        var dbMan = IoCManager.Resolve<IServerDbManager>();

        serverName = serverName[..Math.Min(serverName.Length, 1500)];

        var gameTicker = _entitySystemManager.GetEntitySystem<GameTicker>();
        var round = gameTicker.RunLevel switch
        {
            GameRunLevel.PreRoundLobby => gameTicker.RoundId == 0
                ? "pre-round lobby after server restart" // first round after server restart has ID == 0
                : $"pre-round lobby for round {gameTicker.RoundId + 1}",
            GameRunLevel.InRound => $"round {gameTicker.RoundId}",
            GameRunLevel.PostRound => $"post-round {gameTicker.RoundId}",
            _ => throw new ArgumentOutOfRangeException(nameof(gameTicker.RunLevel),
                $"{gameTicker.RunLevel} was not matched."),
        };

        DateTimeOffset? expires = null;
        if (minutes > 0)
        {
            expires = DateTimeOffset.Now + TimeSpan.FromMinutes(minutes);
        }

        if (!string.IsNullOrEmpty(webhookUrl))
        {
            var payload = new WebhookPayload
            {
                Username = serverName,
                AvatarUrl = "",
                Embeds = new List<WebhookEmbed>
                {
                    new WebhookEmbed
                    {
                        Color = 0xffea00,
                        Description = GenerateDepartmentBanDescription(target,
                            admin,
                            minutes,
                            reason,
                            expires,
                            department),
                        Footer = new WebhookEmbedFooter
                        {
                            Text = $"({round})"
                        }
                    }
                }
            };

            await _httpClient.PostAsync($"{webhookUrl}?wait=true",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        }
    }

    private string GenerateDepartmentBanDescription(string target,
        ICommonSession? player,
        uint minutes,
        string reason,
        DateTimeOffset? expires,
        string department)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"### **Департмент-бан **");
        builder.AppendLine($"**Нарушитель:** *{target}*");
        builder.AppendLine($"**Причина:** {reason}");

        var banDuration = TimeSpan.FromMinutes(minutes);

        builder.Append($"**Длительность:** ");

        if (expires != null)
        {
            builder.Append($"{banDuration.Days} {NumWord(banDuration.Days, "день", "дня", "дней")}, ");
            builder.Append($"{banDuration.Hours} {NumWord(banDuration.Hours, "час", "часа", "часов")}, ");
            builder.AppendLine($"{banDuration.Minutes} {NumWord(banDuration.Minutes, "минута", "минуты", "минут")}");
        }
        else
        {
            builder.AppendLine($"***Навсегда***");
        }

        builder.AppendLine($"**Отдел:** {department}");

        if (expires != null)
        {
            builder.AppendLine($"**Дата снятия наказания:** {expires}");
        }

        builder.Append($"**Наказание выдал(-а):** ");

        if (player != null)
        {
            builder.AppendLine($"*{player.Name}*");
        }
        else
        {
            builder.AppendLine($"***СИСТЕМА***");
        }

        return builder.ToString();
    }

    private string GenerateBanDescription(string? target,
        string? admin,
        uint minutes,
        string reason,
        DateTimeOffset? expires,
        string? job,
        int color,
        string ban_type)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"### **{ban_type}**");
        builder.AppendLine($"**Нарушитель:** *{target}*");
        builder.AppendLine($"**Причина:** {reason}");

        builder.Append($"**Длительность:** ");

        if (expires != null)
        {
            var banDuration = TimeSpan.FromMinutes(minutes);
            builder.Append($"{banDuration.Days} {NumWord(banDuration.Days, "день", "дня", "дней")}, ");
            builder.Append($"{banDuration.Hours} {NumWord(banDuration.Hours, "час", "часа", "часов")}, ");
            builder.AppendLine($"{banDuration.Minutes} {NumWord(banDuration.Minutes, "минута", "минуты", "минут")}");
        }
        else
        {
            builder.AppendLine($"***Навсегда***");
        }

        if (job != null)
        {
            builder.AppendLine($"**Роль:** {job}");
        }

        if (expires != null)
        {
            builder.AppendLine($"**Дата снятия наказания:** {expires}");
        }

        builder.Append($"**Наказание выдал(-а):** ");

        if (admin != null)
        {
            builder.AppendLine($"*{admin}*");
        }
        else
        {
            builder.AppendLine($"***СИСТЕМА***");
        }

        return builder.ToString();
    }

    private string NumWord(int value, params string[] words)
    {
        value = Math.Abs(value) % 100;
        var num = value % 10;

        if (value > 10 && value < 20)
        {
            return words[2];
        }

        if (value > 1 && value < 5)
        {
            return words[1];
        }

        if (num == 1)
        {
            return words[0];
        }

        return words[2];
    }
}

using System.Net;
using System.Net.Http;
using System.Text;
using FC.Engine.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Tests.Services;

public class AfricasTalkingSmsSenderTests
{
    [Theory]
    [InlineData("0803 123 4567", "+2348031234567")]
    [InlineData("2348031234567", "+2348031234567")]
    [InlineData("+2348031234567", "+2348031234567")]
    public void Sms_Normalizes_Nigerian_Phone_Numbers(string input, string expected)
    {
        var normalized = AfricasTalkingSmsSender.NormalizeNigerianPhone(input);
        normalized.Should().Be(expected);
    }

    [Fact]
    public async Task Sms_Under_160_Characters()
    {
        var handler = new CaptureHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.africastalking.com")
        };

        var settings = Options.Create(new NotificationSettings
        {
            Sms = new SmsSettings
            {
                Provider = "AfricasTalking",
                AfricasTalking = new AfricasTalkingSettings
                {
                    Username = "regos",
                    ApiKey = "atsk_test",
                    SenderId = "RegOS"
                }
            }
        });

        var sut = new AfricasTalkingSmsSender(client, settings);
        var message = new string('A', 220);

        var result = await sut.SendAsync("08031234567", message);

        result.Success.Should().BeTrue();
        handler.CapturedFields.Should().ContainKey("message");
        handler.CapturedFields["message"].Length.Should().BeLessThanOrEqualTo(160);
        handler.CapturedFields["to"].Should().Be("+2348031234567");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Dictionary<string, string> CapturedFields { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (var pair in content.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = pair.Split('=', 2);
                var key = WebUtility.UrlDecode(split[0]);
                var value = split.Length > 1 ? WebUtility.UrlDecode(split[1]) : string.Empty;
                CapturedFields[key] = value;
            }

            var json = "{\"SMSMessageData\":{\"Recipients\":[{\"status\":\"Success\",\"messageId\":\"msg-1\",\"cost\":\"NGN 4.5000\"}]}}";
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}

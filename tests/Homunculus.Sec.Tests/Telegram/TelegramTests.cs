using System.Text.Json.Nodes;
using Homunculus.Sec.Telegram;

namespace Homunculus.Sec.Tests.Telegram;

public class TelegramTests
{
    [Test]
    public void ParsesMessagesAndButtonPresses()
    {
        var result = JsonNode.Parse(
            """
            [
              {"update_id":10,"message":{"message_id":5,"from":{"id":42},"chat":{"id":42},"text":"yes","reply_to_message":{"message_id":3}}},
              {"update_id":11,"callback_query":{"id":"abc","from":{"id":42},"message":{"message_id":3,"chat":{"id":42}},"data":"a:7"}},
              {"update_id":12,"edited_message":{"text":"ignored kind"}}
            ]
            """);

        var updates = TelegramApi.ParseUpdates(result);

        Assert.That(updates, Has.Count.EqualTo(3));
        Assert.That(updates[0].Message, Is.EqualTo(new IncomingMessage(42, 42, 5, "yes", 3)));
        Assert.That(updates[1].Press, Is.EqualTo(new ButtonPress("abc", 42, 42, 3, "a:7")));
        Assert.That(updates[2].Message, Is.Null);
        Assert.That(updates[2].Press, Is.Null);
        Assert.That(updates[2].Id, Is.EqualTo(12));
    }

    [Test]
    public void ParsingAnythingUnexpectedGivesNoUpdates()
    {
        Assert.That(TelegramApi.ParseUpdates(null), Is.Empty);
        Assert.That(TelegramApi.ParseUpdates(JsonNode.Parse("{}")), Is.Empty);
    }
}

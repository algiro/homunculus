using Homunculus.Sec.Safety;

namespace Homunculus.Sec.Analysis;

/// <summary>The last check before a proposal reaches the user: whatever the model said, a protected address is never blocked and a block always has a sane length.</summary>
public sealed class ProposalGuard(Allowlist allowlist, TimeSpan? maxBlock = null)
{
    public static readonly TimeSpan DefaultBlock = TimeSpan.FromHours(24);
    public static readonly TimeSpan NetworkMaxBlock = TimeSpan.FromHours(48);

    private readonly TimeSpan _max = maxBlock ?? TimeSpan.FromDays(7);

    public bool IsProtected(string ip) => allowlist.IsProtected(ip);

    public Proposal Apply(Proposal proposal)
    {
        if (proposal.Action == ActionKind.BlockIp && allowlist.IsProtected(proposal.Ip))
            return proposal with
            {
                Action = ActionKind.Ignore,
                Reason = $"{proposal.Reason} (Not blocked: {proposal.Ip} is on the never-block list.)",
            };

        if (proposal.Action == ActionKind.Ignore && proposal.Finding.Hard)
        {
            // The model can be talked into "ignore" by whoever wrote the log text (a user agent that claims to be a search engine, say).
            // Evidence no ordinary visitor produces always reaches the operator, who decides.
            return proposal with
            {
                Action = ActionKind.BlockIp,
                Severity = proposal.Severity < Severity.Medium ? Severity.Medium : proposal.Severity,
                BlockFor = DefaultBlock,
                Reason = $"{proposal.Reason} (The model suggested ignoring this, but the evidence includes injection strings, paths only scanners request or unusual request methods, so it is shown to you to decide.)",
            };
        }

        var blockFor = proposal.BlockFor <= TimeSpan.Zero ? DefaultBlock : proposal.BlockFor > _max ? _max : proposal.BlockFor;

        // A network may hold innocent users, so a block of one is kept short; it can always be renewed.
        if (Cidr.IsNetwork(proposal.Ip) && blockFor > NetworkMaxBlock) blockFor = NetworkMaxBlock;
        return proposal with { BlockFor = blockFor };
    }
}

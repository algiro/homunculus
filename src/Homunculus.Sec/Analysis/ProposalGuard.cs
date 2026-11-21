using Homunculus.Sec.Safety;

namespace Homunculus.Sec.Analysis;

/// <summary>The last check before a proposal reaches the user: whatever the model said, a protected address is never blocked and a block always has a sane length.</summary>
public sealed class ProposalGuard(Allowlist allowlist, TimeSpan? maxBlock = null)
{
    public static readonly TimeSpan DefaultBlock = TimeSpan.FromHours(24);

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

        var blockFor = proposal.BlockFor <= TimeSpan.Zero ? DefaultBlock : proposal.BlockFor > _max ? _max : proposal.BlockFor;
        return proposal with { BlockFor = blockFor };
    }
}

using Cosmoteer.GalaxyMap;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// Spec 872: the extra lines of a galaxy map node tooltip, kept for `Ux.HudRefreshSeconds` of real time. The game asks for the tooltip of
/// the node under the cursor in every frame; the lines read paths, zones and the lockdown, so they are built at most once per refresh.
/// UI only. One cache for each tooltip patch; a new run clears it.
/// </summary>
internal sealed class TipCache
{
    readonly Dictionary<MapNode, (double At, string Text)> _byNode = [];
    Run? _run;

    public string Get(Run run, MapNode node, Func<string> build)
    {
        if (_run != run) { _byNode.Clear(); _run = run; }
        double now = Halfling.App.Clock.Time.Seconds; // determinism: UI cache age only
        if (_byNode.TryGetValue(node, out var hit) && now - hit.At < UxKit.HudRefreshSeconds) return hit.Text;
        string text = build();
        _byNode[node] = (now, text);
        return text;
    }
}

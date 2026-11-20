/// <summary>
/// Outcome of a startup data-sync pass. The caller uses <see cref="Changed"/>
/// to decide whether the expensive post-sync re-merge (cache invalidation +
/// re-applying versions/rules onto the loaded mods + dictionary warmup) is
/// necessary. A conservative "something was written" signal is intentional:
/// a false positive only costs an extra re-merge, a false negative would
/// leave stale data on screen.
/// </summary>
public sealed class DataUpdateResult
{
    /// <summary>
    /// True when at least one tracked category (curated manifest files,
    /// external versions, rules, replacements or ModInfo) was actually
    /// written during this pass. 304 / matching-SHA outcomes do not set it.
    /// </summary>
    public bool Changed { get; init; }
}

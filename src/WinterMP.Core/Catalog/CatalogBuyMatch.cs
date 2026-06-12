namespace WinterMP.Core.Catalog
{
    /// <summary>Matched buy rule from sync-catalog.json (entry guards + result states).</summary>
    public sealed class CatalogBuyMatch
    {
        public CatalogBuyMatch(CatalogBuyGuard[] entryGuards, string[] resultStates)
        {
            EntryGuards = entryGuards;
            ResultStates = resultStates;
        }

        public CatalogBuyGuard[] EntryGuards { get; }
        public string[] ResultStates { get; }
    }

    public struct CatalogBuyGuard
    {
        public string StateName;
        public string TriggerEvent;
    }
}

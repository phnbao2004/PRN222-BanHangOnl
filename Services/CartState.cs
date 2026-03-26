namespace MiniShopee.Services;

public class CartState
{
    public Dictionary<int, int> Items { get; } = new();

    public int TotalCount => Items.Values.Sum();

    public void Add(int productId)
    {
        if (!Items.TryAdd(productId, 1))
            Items[productId]++;
    }

    public void Remove(int productId)
    {
        if (!Items.ContainsKey(productId)) return;
        Items[productId]--;
        if (Items[productId] <= 0)
            Items.Remove(productId);
    }

    public void RemoveAll(int productId) => Items.Remove(productId);

    public void Clear() => Items.Clear();

    public event Action? OnChange;
    public void NotifyChange() => OnChange?.Invoke();
}

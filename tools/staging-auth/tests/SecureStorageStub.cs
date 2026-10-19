using System.Collections.Concurrent;
using System.Threading.Tasks;
// In-memory test fake: never linked into Android production source.
namespace Microsoft.Maui.Storage;
public sealed class TestStore
{
    private readonly ConcurrentDictionary<string,string> store = new();
    public Task<string?> GetAsync(string key) => Task.FromResult(store.TryGetValue(key,out var value)?value:null);
    public Task SetAsync(string key,string value) { store[key]=value;return Task.CompletedTask; }
    public bool Remove(string key)=>store.TryRemove(key,out _);
}
public static class SecureStorage { public static TestStore Default { get; }=new(); }

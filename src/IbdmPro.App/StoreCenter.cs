using System.Net.Http;
using IbdmPro.Core;

namespace IbdmPro.App;

/// <summary>Keeps the side bar's slide show up to date with the newest programs and games on IBRA STORE:
/// the saved copy at once, then the site now and every hour. If the site can't be read the last copy stays
/// (and with no copy at all the slide show only has the developer's ads, or is hidden).</summary>
public static class StoreCenter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public static List<StoreApp> Latest { get; private set; } = [];
    public static void SetForPreview(List<StoreApp> list) => Latest = list;

    public static async Task RunInBackground()
    {
        Latest = StoreFeed.Cached(L.Lang);
        if (Latest.Count > 0) Refresh();
        await Task.Delay(TimeSpan.FromSeconds(5));
        while (true)
        {
            try
            {
                var list = await StoreFeed.FetchAsync(Http, L.Lang);
                if (list.Count > 0) { Latest = list; Refresh(); }
            }
            catch (Exception e) { Log.Write($"store: {e.Message}"); }
            await Task.Delay(TimeSpan.FromHours(1));
        }
    }

    private static void Refresh()
    {
        try { IbdmApp.Instance.Dispatcher.Invoke(() => IbdmApp.Instance.Main?.RefreshAd()); }
        catch (Exception e) { Log.Write($"store: {e.Message}"); }
    }
}

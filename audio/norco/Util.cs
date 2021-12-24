namespace norco;

internal static class Util
{
    public static Uri ProcessNorcoUri(string uriStr)
    {
        int indexFragment = uriStr.IndexOf('#');
        UriBuilder ub = new() { Scheme = "file", Host = "", Path = Path.GetFullPath(indexFragment != -1 ? uriStr[..indexFragment] : uriStr), Fragment = indexFragment != -1 ? uriStr[(indexFragment + 1)..] : null };
        return ub.Uri;
    }
}

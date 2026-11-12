using System.Text;

namespace NitroDrp.Api.Email;

public static class EmailAddressFormat
{
    /// <summary>
    /// "Jaded Media" &lt;transfers@jadedmedia.ca&gt;. Plain names are quoted; anything non-ASCII is sent as an
    /// RFC 2047 encoded word, so a name like "Réception" survives. Control characters are dropped so a name can't
    /// smuggle in extra headers.
    /// </summary>
    public static string Display(string name, string address)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return address;
        if (clean.All(c => c < 128))
            return $"\"{clean.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" <{address}>";
        return $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(clean))}?= <{address}>";
    }
}

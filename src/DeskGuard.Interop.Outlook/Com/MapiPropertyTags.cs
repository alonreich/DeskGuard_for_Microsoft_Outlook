namespace DeskGuard.Interop.Outlook.Com;

public static class MapiPropertyTags
{
    public const string SchemaPrefix = "http://schemas.microsoft.com/mapi/proptag/";

    public const string PrSubjectW = "0x0037001F";
    public const string PrSubjectA = "0x0037001E";
    public const string PrConversationTopicW = "0x0070001F";
    public const string PrConversationTopicA = "0x0070001E";
    public const string PrTransportMessageHeadersW = "0x007D001F";
    public const string PrTransportMessageHeadersA = "0x007D001E";
    public const string PrSenderSmtpAddressW = "0x5D01001F";
    public const string PrSenderSmtpAddressA = "0x5D01001E";
    public const string PrSentRepresentingSmtpAddressW = "0x5D07001F";
    public const string PrSentRepresentingSmtpAddressA = "0x5D07001E";
    public const string PrSenderEmailAddressW = "0x0065001F";
    public const string PrSenderEmailAddressA = "0x0065001E";
    public const string PrSentRepresentingNameW = "0x0042001F";
    public const string PrSentRepresentingNameA = "0x0042001E";
    public const string PrInternetMessageId = "0x1035001F";
    public const string PrSearchKey = "0x300B0102";
    public const string PrSmtpAddressA = "0x39FE001E";
    public const string PrMessageDeliveryTime = "0x0E060040";
    public const string PrMessageSize = "0x0E080003";
    public const string PrMessageClass = "0x001A001F";
    public const string PrEntryId = "0x0FFF0102";

    public static string ToPropTagUrl(string hexTag) => $"{SchemaPrefix}{hexTag}";
}

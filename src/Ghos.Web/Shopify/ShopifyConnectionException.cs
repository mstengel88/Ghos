namespace Ghos.Web.Shopify;

public sealed class ShopifyConnectionException(string message, Exception? innerException = null)
    : Exception(message, innerException);

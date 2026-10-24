using System;
using System.Security.Cryptography;

namespace GameMod.VersionHandling
{
    // Release indexes are signed offline (PYRO-RELEASES/signing/sign_index.py) and
    // verified here against the shipped key, because UnityWebRequest on this engine
    // version validates no TLS certificate — the signature is the trust root, not
    // the transport. The signature covers the exact index bytes as served.
    public static class SignedIndex
    {
        const string PUBLIC_KEY_XML = "<RSAKeyValue><Modulus>mdiitM7OsvMZiuLoobDpvAo2/wCEsUlGnN+abAqITwRAfnGtJFA4YVlaIqF9oX8V6c4Flxkyv0y+iAcmTUy+77EFyk8uz8ED16GnOPhtSLpsQFoFkFGkae9LPA9EBfHEmqgyPckjXs0wJA6xCOUsN064JKsCz58AFXKiFcwiz2rtKC8dRolLO6y/AqVMlra9C5DyhD7jQAQ2aOPzZPPFpfmRiaz3j6cCOWklAwMCGX4DOBO0dy/B5j8RwwppvAx8dJKouHUIhb6YcQAaph6visB2JZhO7cTAB/oMjOT3TG24bRrMRzk+11EQvgJ1TBvSVqAV7Z7BwkQgcxJW+nhADQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static bool Verify(byte[] indexBytes, string signatureBase64)
        {
            if (indexBytes == null || string.IsNullOrEmpty(signatureBase64))
                return false;
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(signatureBase64.Trim());
            }
            catch (FormatException)
            {
                return false;
            }
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(PUBLIC_KEY_XML);
                try
                {
                    return rsa.VerifyData(indexBytes, "SHA256", signature);
                }
                catch (CryptographicException)
                {
                    return false;
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Security.Cryptography;

namespace SaveSystem
{
    /**
     * <summary>Encrypts a save with AES and a key the game supplies, with a fresh random IV for every write.</summary>
     * <remarks>The key ships with the game, so this stops casual editing, not a determined player.</remarks>
     */
    public sealed class AesTransform : ISaveTransform
    {
        #region Private Fields

        private const int IvLength = 16;
        private readonly byte[] _key;

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public string Id => "aes";

        #endregion

        #region Public API

        /**
         * <param name="key">16, 24 or 32 bytes.</param>
         * <exception cref="ArgumentException">The key has another length.</exception>
         */
        public AesTransform(byte[] key)
        {
            if (key == null || (key.Length != 16 && key.Length != 24 && key.Length != 32))
                throw new ArgumentException("An AES key must be 16, 24 or 32 bytes.", nameof(key));
            _key = (byte[])key.Clone();
        }

        /** <inheritdoc /> */
        public byte[] Apply(byte[] data)
        {
            using (var aes = CreateAes())
            {
                aes.GenerateIV();
                using (var output = new MemoryStream())
                {
                    output.Write(aes.IV, 0, IvLength);
                    using (var encryptor = aes.CreateEncryptor())
                    using (var crypto = new CryptoStream(output, encryptor, CryptoStreamMode.Write))
                        crypto.Write(data, 0, data.Length);
                    return output.ToArray();
                }
            }
        }

        /** <inheritdoc /> */
        public byte[] Revert(byte[] data)
        {
            if (data.Length < IvLength) throw new CryptographicException("Encrypted data is too short.");
            using (var aes = CreateAes())
            {
                var iv = new byte[IvLength];
                Array.Copy(data, iv, IvLength);
                aes.IV = iv;
                using (var decryptor = aes.CreateDecryptor())
                using (var input = new CryptoStream(new MemoryStream(data, IvLength, data.Length - IvLength), decryptor, CryptoStreamMode.Read))
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        #endregion

        #region Helpers

        private Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Key = _key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        #endregion
    }
}

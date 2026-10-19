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
        private static readonly int[] KeyLengths = { 16, 24, 32 };
        private readonly byte[] _key;

        #endregion

        #region Properties

        /** <summary>The id an AES transform has unless given another.</summary> */
        public const string DefaultId = "aes";

        /** <inheritdoc /> */
        public string Id { get; }

        #endregion

        #region Public API

        /**
         * <summary>Makes a transform with a key.</summary>
         * <param name="key">16, 24 or 32 bytes.</param>
         * <param name="id">Written in each file's header. Give a new key a new id (<c>aes-2</c>) and keep the old one
         * readable, so saves made with the old key still load.</param>
         * <exception cref="ArgumentException">The key has another length.</exception>
         */
        public AesTransform(byte[] key, string id = DefaultId)
        {
            if (key == null || Array.IndexOf(KeyLengths, key.Length) < 0)
                throw new ArgumentException("An AES key must be 16, 24 or 32 bytes.", nameof(key));
            _key = (byte[])key.Clone();
            Id = id;
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

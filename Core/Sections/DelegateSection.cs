using System;

namespace SaveSystem
{
    /**
     * <summary>A section made from its key and two functions: what to capture and how to restore it. For state that
     * already knows how to capture and restore itself, so it needs no section class of its own.</summary>
     * <typeparam name="TData">The data class. Its fields are the section's schema on disk.</typeparam>
     */
    public sealed class DelegateSection<TData> : SaveSection<TData> where TData : class, new()
    {
        #region Private Fields

        private readonly string _key;
        private readonly Func<TData> _capture;
        private readonly Action<TData> _restore;
        private readonly Func<TData> _createDefault;

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public override string Key => _key;

        #endregion

        #region Public API

        /**
         * <summary>A section saved under <paramref name="key" />, captured by <paramref name="capture" /> and restored by
         * <paramref name="restore" />; a new game starts from <paramref name="createDefault" />, or a new
         * <typeparamref name="TData" /> with none.</summary>
         */
        public DelegateSection(string key, Func<TData> capture, Action<TData> restore, Func<TData> createDefault = null)
        {
            _key = !string.IsNullOrEmpty(key) ? key : throw new ArgumentException("A section needs a key.", nameof(key));
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _restore = restore ?? throw new ArgumentNullException(nameof(restore));
            _createDefault = createDefault;
        }

        #endregion

        #region Helpers

        /** <inheritdoc /> */
        protected override TData Capture() => _capture();

        /** <inheritdoc /> */
        protected override void Restore(TData data) => _restore(data);

        /** <inheritdoc /> */
        protected override TData CreateDefault() => _createDefault != null ? _createDefault() : base.CreateDefault();

        #endregion
    }
}

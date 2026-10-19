using System;

namespace SaveSystem
{
    /**
     * <summary>A one-time change across a whole save: renaming a section's key, splitting one section into two,
     * merging two, or dropping a retired one.</summary>
     * <remarks>
     * Each runs once per save, in the order added, before any section reads its data. A save records the ids of the
     * upgrades it has had, so ids must never change or be reused.
     * </remarks>
     */
    public sealed class DocumentUpgrade
    {
        #region Private Fields

        private readonly Action<SaveDocument> _apply;

        #endregion

        #region Properties

        /** <summary>The upgrade's name, recorded in every save it has run on.</summary> */
        public string Id { get; }

        #endregion

        #region Public API

        /**
         * <summary>Makes an upgrade.</summary>
         * <param name="id">Follows the section key rules.</param>
         * <param name="apply">Changes the document's sections. It receives a copy, so a failure changes nothing.</param>
         * <exception cref="ArgumentException">The id is invalid.</exception>
         */
        public DocumentUpgrade(string id, Action<SaveDocument> apply)
        {
            if (!SaveNames.IsValidSectionKey(id)) throw new ArgumentException($"Invalid upgrade id '{id}'.", nameof(id));
            Id = id;
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        /** <summary>Changes the document.</summary> */
        public void Apply(SaveDocument document) => _apply(document);

        #endregion
    }
}

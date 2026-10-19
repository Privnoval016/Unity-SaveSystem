using Newtonsoft.Json.Linq;

namespace SaveSystem
{
    /** <summary>One section as stored: the version its data was written at, and the data.</summary> */
    public sealed class SectionRecord
    {
        #region Properties

        /** <summary>The version the data was written at.</summary> */
        public int Version { get; }

        /** <summary>The section's data. Never changed after the record is made.</summary> */
        public JToken Data { get; }

        #endregion

        #region Public API

        /** <summary>Makes a record. The data is used as given, so pass a token nothing else will change.</summary> */
        public SectionRecord(int version, JToken data)
        {
            Version = version;
            Data = data ?? JValue.CreateNull();
        }

        #endregion
    }
}

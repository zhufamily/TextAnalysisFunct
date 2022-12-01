using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextAnalysisFunct
{
    /// <summary>
    /// Durable parameter for text analysis
    /// </summary>
    public class DurableParam
    {
        /// <summary>
        /// Public constructor
        /// </summary>
        public DurableParam() { }

        /// <summary>
        /// Key from Cognitive Services
        /// </summary>
        public string Key { get; set; }
        /// <summary>
        /// Region from Cognitive Services
        /// </summary>
        public string Region { get; set; }
        /// <summary>
        /// Url for Cognitive Services
        /// </summary>
        public string Url { get; set; }
        /// <summary>
        /// Text analysis method
        /// </summary>
        public string Method { get; set; }
        /// <summary>
        /// Raw text for analysis
        /// </summary>
        public string JsonBody { get; set; }
        /// <summary>
        /// Max allowed chunk size in characters
        /// </summary>
        public int ChunkSize { get; set; }
        /// <summary>
        /// Splitors -- paragraph by default
        /// </summary>
        public string[] Splitors { get; set; }
    }

    /// <summary>
    /// Durable parameter for generating chunks
    /// </summary>
    public class ChunkParam
    { 
        /// <summary>
        /// Public constructor
        /// </summary>
        public ChunkParam() { }
        
        /// <summary>
        /// Raw text for chunking
        /// </summary>
        public string LongText { get; set; }
        /// <summary>
        /// Max allowed chunk size in characters
        /// </summary>
        public int ChunkSize { get; set; }
        /// <summary>
        /// Splitors -- paragraph by default
        /// </summary>
        public string[] Splitors { get; set; }
    }
}

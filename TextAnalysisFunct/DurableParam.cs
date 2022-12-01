using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextAnalysisFunct
{
    public class DurableParam
    {
        public DurableParam() { }

        public string Key { get; set; }
        public string Region { get; set; }
        public string Url { get; set; }
        public string Method { get; set; }
        public string JsonBody { get; set; }
        public int ChunkSize { get; set; }
        public string[] Splitors { get; set; }
    }

    public class ChunkParam
    { 
        public ChunkParam() { }
        public string LongText { get; set; }
        public int ChunkSize { get; set; }
        public string[] Splitors { get; set; }
    }
}

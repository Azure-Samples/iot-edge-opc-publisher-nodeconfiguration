
using Opc.Ua;
using System;
using System.Collections.Generic;

namespace OpcPublisher
{
    public class NodeLookup
    {
        public NodeLookup()
        {
        }

        public Uri EndPointURL;

        public NodeId NodeID;
    }

    public class PublishedNodesCollection : List<NodeLookup>
    {
        public PublishedNodesCollection()
        {
        }
    }

    public class NodeIdInfo
    {
        public NodeIdInfo(string id)
        {
            Id = id;
            Published = false;
        }

        public string Id { get; set; }

        public bool Published;
    }
}

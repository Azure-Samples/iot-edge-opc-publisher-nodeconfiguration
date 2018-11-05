using Newtonsoft.Json;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.ComponentModel;

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

    //    public class OpcNodeOnEndpoint
    //    {
    //        // Id can be:
    //        // a NodeId ("ns=")
    //        // an ExpandedNodeId ("nsu=")
    //        public string Id;

    //        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    //        public int? OpcSamplingInterval;

    //        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    //        public int? OpcPublishingInterval;
    //    }


    //    public class PublisherConfigurationFileEntry
    //    {
    //        public PublisherConfigurationFileEntry()
    //        {
    //        }

    //        public PublisherConfigurationFileEntry(string nodeId, string endpointUrl)
    //        {
    //            EndpointUrl = new Uri(endpointUrl);
    //            OpcNodes = new List<OpcNodeOnEndpoint>();
    //        }

    //        public Uri EndpointUrl { get; set; }

    //        [DefaultValue(true)]
    //        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore)]
    //        public bool? UseSecurity { get; set; }

    //        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    //        public List<OpcNodeOnEndpoint> OpcNodes { get; set; }
    //    }
}

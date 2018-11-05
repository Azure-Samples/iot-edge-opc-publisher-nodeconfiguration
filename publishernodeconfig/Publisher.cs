using Newtonsoft.Json;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Threading;

namespace PubisherConfig
{
    using Microsoft.Azure.Devices;
    using OpcPublisher;
    using System.Net;
    using static Program;

    public class Publisher
    {
        public Publisher(string iotHubConnectionString, string iotHubPublisherDeviceName, string iotHubPublisherModuleName, int maxShortWaitSec, int maxLongWaitSec, CancellationToken ct)
        {
            // init IoTHub connection
            _iotHubClient = ServiceClient.CreateFromConnectionString(iotHubConnectionString, TransportType.Amqp_WebSocket_Only);
            _publisherDeviceName = iotHubPublisherDeviceName;
            _publisherDevice = new Device(iotHubPublisherDeviceName);
            _publisherModule = null;
            if (!string.IsNullOrEmpty(iotHubPublisherModuleName))
            {
                _publisherModuleName = iotHubPublisherModuleName;
                _publisherModule = new Module(iotHubPublisherDeviceName, iotHubPublisherModuleName);
            }
            TimeSpan responseTimeout = TimeSpan.FromSeconds(300);
            TimeSpan connectionTimeout = TimeSpan.FromSeconds(30);
            _publishNodesMethod = new CloudToDeviceMethod("PublishNodes", responseTimeout, connectionTimeout);
            _unpublishNodesMethod = new CloudToDeviceMethod("UnpublishNodes", responseTimeout, connectionTimeout);
            _unpublishAllNodesMethod = new CloudToDeviceMethod("UnpublishAllNodes", responseTimeout, connectionTimeout);
            _getConfiguredEndpointsMethod = new CloudToDeviceMethod("GetConfiguredEndpoints", responseTimeout, connectionTimeout);
            _getConfiguredNodesOnEndpointMethod = new CloudToDeviceMethod("GetConfiguredNodesOnEndpoint", responseTimeout, connectionTimeout);
        }

        public bool PublishNodes(List<NodeIdInfo> nodeIdInfos, CancellationToken ct, string endpointUrl = null)
        {
            bool result = true;
            int retryCount = MAX_RETRY_COUNT;

            try
            {
                PublishNodesMethodRequestModel publishNodesMethodRequestModel = new PublishNodesMethodRequestModel(endpointUrl);
                foreach (var nodeIdInfo in nodeIdInfos)
                {
                    publishNodesMethodRequestModel.Nodes.Add(new NodeModel(nodeIdInfo.Id));
                }
                CloudToDeviceMethodResult methodResult = new CloudToDeviceMethodResult();
                methodResult.Status = (int)HttpStatusCode.NotAcceptable;
                while (methodResult.Status == (int)HttpStatusCode.NotAcceptable && retryCount-- > 0)
                {
                    _publishNodesMethod.SetPayloadJson(JsonConvert.SerializeObject(publishNodesMethodRequestModel));
                    if (string.IsNullOrEmpty(_publisherModuleName))
                    {
                        methodResult = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publishNodesMethod, ct).Result;
                    }
                    else
                    {
                        methodResult = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _publishNodesMethod, ct).Result;
                    }
                    if (methodResult.Status == (int)HttpStatusCode.NotAcceptable)
                    {
                        Thread.Sleep(MAX_SHORT_WAIT_SEC * 1000);
                    }
                    else
                    {
                        break;
                    }
                }
                foreach (var nodeIdInfo in nodeIdInfos)
                {
                    if (!nodeIdInfo.Published && methodResult.Status != (int)HttpStatusCode.OK && methodResult.Status != (int)HttpStatusCode.Accepted)
                    {
                        Logger.Warning($"PublishNodes failed (nodeId: '{nodeIdInfo.Id}', statusCode: '{methodResult.Status}', publishedState: '{nodeIdInfo.Published}')");
                        result = false;
                    }
                    else
                    {
                        nodeIdInfo.Published = true;
                        Logger.Debug($"PublishNodes succeeded (nodeId: '{nodeIdInfo.Id}', statusCode: '{methodResult.Status}')");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, $"PublishNodes Exception");
            }
            return result;
        }

        public bool UnpublishNodes(List<NodeIdInfo> nodeIdInfos, CancellationToken ct, string endpointUrl)
        {
            try
            {
                UnpublishNodesMethodRequestModel unpublishNodesMethodRequestModel = new UnpublishNodesMethodRequestModel(endpointUrl);
                foreach (var nodeIdInfo in nodeIdInfos)
                {
                    unpublishNodesMethodRequestModel.Nodes.Add(new NodeModel(nodeIdInfo.Id));
                }
                _unpublishNodesMethod.SetPayloadJson(JsonConvert.SerializeObject(unpublishNodesMethodRequestModel));
                CloudToDeviceMethodResult result;
                if (string.IsNullOrEmpty(_publisherModuleName))
                {
                    result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _unpublishNodesMethod, ct).Result;
                }
                else
                {
                    result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _unpublishNodesMethod, ct).Result;
                }
                foreach (var nodeIdInfo in nodeIdInfos)
                {
                    if (nodeIdInfo.Published && result.Status != (int)HttpStatusCode.OK && result.Status != (int)HttpStatusCode.Accepted)
                    {
                        Logger.Warning($"UnpublishNodes failed (nodeId: '{nodeIdInfo.Id}', statusCode: '{result.Status}', publishedState: '{nodeIdInfo.Published}')");
                        return false;
                    }
                    else
                    {
                        nodeIdInfo.Published = false;
                        Logger.Debug($"UnpublishNodes succeeded (nodeId: '{nodeIdInfo.Id}', statusCode: '{result.Status}'");
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, $"UnpublishNodes Exception");
            }
            return false;
        }

        public void UnpublishAllNodes(CancellationToken ct, string endpointUrl = null)
        {
            List<string> endpoints = new List<string>();
            try
            {
                UnpublishAllNodesMethodRequestModel unpublishAllNodesMethodRequestModel = new UnpublishAllNodesMethodRequestModel();
                CloudToDeviceMethodResult result;
                if (string.IsNullOrEmpty(_publisherModuleName))
                {
                    result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _unpublishAllNodesMethod, ct).Result;
                }
                else
                {
                    result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _unpublishAllNodesMethod, ct).Result;
                }
                Logger.Debug($"UnpublishAllNodes succeeded, status: '{result.Status}'");
            }
            catch (Exception e)
            {
                Logger.Fatal(e, $"UnpublishAllNodes Exception ");
            }
        }

        public List<string> GetConfiguredEndpoints(CancellationToken ct)
        {
            GetConfiguredEndpointsMethodResponseModel response = null;
            List<string> endpoints = new List<string>();
            try
            {
                GetConfiguredEndpointsMethodRequestModel getConfiguredEndpointsMethodRequestModel = new GetConfiguredEndpointsMethodRequestModel();
                ulong? continuationToken = null;
                while (true)
                {
                    getConfiguredEndpointsMethodRequestModel.ContinuationToken = continuationToken;
                    _getConfiguredEndpointsMethod.SetPayloadJson(JsonConvert.SerializeObject(getConfiguredEndpointsMethodRequestModel));
                    CloudToDeviceMethodResult result;
                    if (string.IsNullOrEmpty(_publisherModuleName))
                    {
                        result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _getConfiguredEndpointsMethod, ct).Result;
                    }
                    else
                    {
                        result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _getConfiguredEndpointsMethod, ct).Result;
                    }
                    response = JsonConvert.DeserializeObject<GetConfiguredEndpointsMethodResponseModel>(result.GetPayloadAsJson());
                    if (response != null && response.Endpoints != null)
                    {
                        endpoints.AddRange(response.Endpoints);
                    }
                    if (response == null || response.ContinuationToken == null)
                    {
                        break;
                    }
                    continuationToken = response.ContinuationToken;
                }
            }
            catch (Exception e)
            {
               Logger.Fatal(e, $"GetConfiguredEndpoints Exception ");
            }
            Logger.Debug($"GetConfiguredEndpoints succeeded, got {endpoints.Count} endpoints");
            return endpoints;
        }

        public List<NodeModel> GetConfiguredNodesOnEndpoint(string endpointUrl, CancellationToken ct)
        {
            GetConfiguredNodesOnEndpointMethodResponseModel response = null;
            List<NodeModel> nodes = new List<NodeModel>();
            try
            {
                GetConfiguredNodesOnEndpointMethodRequestModel getConfiguredNodesOnEndpointMethodRequestModel = new GetConfiguredNodesOnEndpointMethodRequestModel(endpointUrl);
                getConfiguredNodesOnEndpointMethodRequestModel.EndpointUrl = endpointUrl;
                ulong? continuationToken = null;
                while (true)
                {
                    getConfiguredNodesOnEndpointMethodRequestModel.ContinuationToken = continuationToken;
                    _getConfiguredNodesOnEndpointMethod.SetPayloadJson(JsonConvert.SerializeObject(getConfiguredNodesOnEndpointMethodRequestModel));
                    CloudToDeviceMethodResult result;
                    if (string.IsNullOrEmpty(_publisherModuleName))
                    {
                        result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _getConfiguredNodesOnEndpointMethod, ct).Result;
                    }
                    else
                    {
                        result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _getConfiguredNodesOnEndpointMethod, ct).Result;
                    }
                    response = JsonConvert.DeserializeObject<GetConfiguredNodesOnEndpointMethodResponseModel>(result.GetPayloadAsJson());
                    if (response != null && response.Nodes != null)
                    {
                        nodes.AddRange(response.Nodes);
                    }
                    if (response == null || response.ContinuationToken == null)
                    {
                        break;
                    }
                    continuationToken = response.ContinuationToken;
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, $"GetConfiguredNodesOnEndpoint Exception");
            }
            Logger.Debug($"GetConfiguredNodesOnEndpoint succeeded, got {nodes.Count} nodes are published on endpoint '{endpointUrl}')");
            return nodes;
        }

        public bool UnpublishAllConfiguredNodes(CancellationToken ct)
        {
            CloudToDeviceMethodResult result = null;
            List<NodeModel> nodes = new List<NodeModel>();
            try
            {
                UnpublishAllNodesMethodRequestModel unpublishAllNodesMethodRequestModel = new UnpublishAllNodesMethodRequestModel();
                _unpublishAllNodesMethod.SetPayloadJson(JsonConvert.SerializeObject(unpublishAllNodesMethodRequestModel));
                if (string.IsNullOrEmpty(_publisherModuleName))
                {
                    result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _unpublishAllNodesMethod, ct).Result;
                }
                else
                {
                    result = _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _unpublishAllNodesMethod, ct).Result;
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, $"UnpublishAllConfiguredNodes Exception");
            }
            Logger.Debug($"UnpublishAllConfiguredNodes succeeded, result: {(HttpStatusCode)result.Status}");
            return (HttpStatusCode)result.Status == HttpStatusCode.OK ? true : false;
        }

        const int MAX_RETRY_COUNT = 3;

        ServiceClient _iotHubClient;
        string _publisherDeviceName;
        Device _publisherDevice;
        string _publisherModuleName;
        Module _publisherModule;
        CloudToDeviceMethod _publishNodesMethod;
        CloudToDeviceMethod _unpublishNodesMethod;
        CloudToDeviceMethod _unpublishAllNodesMethod;
        CloudToDeviceMethod _getConfiguredEndpointsMethod;
        CloudToDeviceMethod _getConfiguredNodesOnEndpointMethod;
    }
}


using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;

namespace PubisherConfig
{
    using Microsoft.Azure.Devices;
    using OpcPublisher;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using static Program;

    public class Publisher
    {
        public Publisher(string iotHubConnectionString, string iotHubPublisherDeviceName, string iotHubPublisherModuleName)
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
            TimeSpan connectionTimeout = TimeSpan.FromSeconds(120);
            _publishNodesMethod = new CloudToDeviceMethod("PublishNodes", responseTimeout, connectionTimeout);
            _unpublishNodesMethod = new CloudToDeviceMethod("UnpublishNodes", responseTimeout, connectionTimeout);
            _unpublishAllNodesMethod = new CloudToDeviceMethod("UnpublishAllNodes", responseTimeout, connectionTimeout);
            _getConfiguredEndpointsMethod = new CloudToDeviceMethod("GetConfiguredEndpoints", responseTimeout, connectionTimeout);
            _getConfiguredNodesOnEndpointMethod = new CloudToDeviceMethod("GetConfiguredNodesOnEndpoint", responseTimeout, connectionTimeout);
            _getInfoMethod = new CloudToDeviceMethod("GetInfo", responseTimeout, connectionTimeout);
        }

        public async Task<bool> PublishNodesAsync(List<OpcNodeOnEndpointModel> nodesToPublish, CancellationToken ct, string endpointUrl = null)
        {
            bool result = false;
            int retryCount = MAX_RETRY_COUNT;

            try
            {
                PublishNodesMethodRequestModel publishNodesMethodRequestModel = new PublishNodesMethodRequestModel(endpointUrl);
                publishNodesMethodRequestModel.OpcNodes.AddRange(nodesToPublish);
                CloudToDeviceMethodResult methodResult = new CloudToDeviceMethodResult();
                methodResult.Status = (int)HttpStatusCode.NotAcceptable;
                while (methodResult.Status == (int)HttpStatusCode.NotAcceptable && retryCount-- > 0)
                {
                    _publishNodesMethod.SetPayloadJson(JsonConvert.SerializeObject(publishNodesMethodRequestModel));
                    if (string.IsNullOrEmpty(_publisherModuleName))
                    {
                        methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publishNodesMethod, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _publishNodesMethod, ct).ConfigureAwait(false);
                    }
                    if (methodResult.Status == (int)HttpStatusCode.NotAcceptable)
                    {
                        Thread.Sleep(MAXSHORTWAITSEC * 1000);
                    }
                    else
                    {
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }
                        LogMethodResult(methodResult, _publishNodesMethod.MethodName);
                        result = methodResult.Status == (int)HttpStatusCode.OK;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Exception");
            }
            return result;
        }

        public async Task<bool> UnpublishNodesAsync(List<OpcNodeOnEndpointModel> nodesToUnpublish, string endpointUrl, CancellationToken ct)
        {
            bool result = false;
            try
            {
                UnpublishNodesMethodRequestModel unpublishNodesMethodRequestModel = new UnpublishNodesMethodRequestModel(endpointUrl);
                unpublishNodesMethodRequestModel.OpcNodes.AddRange(nodesToUnpublish);
                _unpublishNodesMethod.SetPayloadJson(JsonConvert.SerializeObject(unpublishNodesMethodRequestModel));
                CloudToDeviceMethodResult methodResult;
                if (string.IsNullOrEmpty(_publisherModuleName))
                {
                    methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _unpublishNodesMethod, ct).ConfigureAwait(false);
                }
                else
                {
                    methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _unpublishNodesMethod, ct).ConfigureAwait(false);
                }
                if (!ct.IsCancellationRequested)
                {
                    LogMethodResult(methodResult, _publishNodesMethod.MethodName);
                    result = methodResult.Status == (int)HttpStatusCode.OK;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Exception");
            }
            return result;
        }

        public async Task<bool> UnpublishAllNodesAsync(CancellationToken ct, string endpointUrl = null)
        {
            bool result = false;
            try
            {
                UnpublishAllNodesMethodRequestModel unpublishAllNodesMethodRequestModel = new UnpublishAllNodesMethodRequestModel();
                CloudToDeviceMethodResult methodResult;
                if (string.IsNullOrEmpty(_publisherModuleName))
                {
                    methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _unpublishAllNodesMethod, ct).ConfigureAwait(false);
                }
                else
                {
                    methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _unpublishAllNodesMethod, ct).ConfigureAwait(false);
                }
                if (!ct.IsCancellationRequested)
                {
                    LogMethodResult(methodResult, _publishNodesMethod.MethodName);
                    result = methodResult.Status == (int)HttpStatusCode.OK;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Exception ");
            }
            return result;
        }

        public async Task<List<string>> GetConfiguredEndpointsAsync(CancellationToken ct)
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
                    CloudToDeviceMethodResult methodResult;
                    if (string.IsNullOrEmpty(_publisherModuleName))
                    {
                        methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _getConfiguredEndpointsMethod, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _getConfiguredEndpointsMethod, ct).ConfigureAwait(false);
                    }
                    if (!ct.IsCancellationRequested)
                    {
                        if (methodResult.Status == (int)HttpStatusCode.OK)
                        {
                            response = JsonConvert.DeserializeObject<GetConfiguredEndpointsMethodResponseModel>(methodResult.GetPayloadAsJson());
                            if (response != null && response.Endpoints != null)
                            {
                                endpoints.AddRange(response.Endpoints.Select(e => e.EndpointUrl));
                            }
                            if (response == null || response.ContinuationToken == null)
                            {
                                break;
                            }
                            continuationToken = response.ContinuationToken;
                        }
                        else
                        {
                            LogMethodResult(methodResult, _publishNodesMethod.MethodName);
                            break; ;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
               Logger.Error(e, $"Exception ");
            }
            return endpoints;
        }

        public async Task<List<OpcNodeOnEndpointModel>> GetConfiguredNodesOnEndpointAsync(string endpointUrl, CancellationToken ct)
        {
            GetConfiguredNodesOnEndpointMethodResponseModel response = null;
            List<OpcNodeOnEndpointModel> nodes = new List<OpcNodeOnEndpointModel>();

            try
            {
                GetConfiguredNodesOnEndpointMethodRequestModel getConfiguredNodesOnEndpointMethodRequestModel = new GetConfiguredNodesOnEndpointMethodRequestModel(endpointUrl);
                getConfiguredNodesOnEndpointMethodRequestModel.EndpointUrl = endpointUrl;
                ulong? continuationToken = null;
                while (true)
                {
                    getConfiguredNodesOnEndpointMethodRequestModel.ContinuationToken = continuationToken;
                    _getConfiguredNodesOnEndpointMethod.SetPayloadJson(JsonConvert.SerializeObject(getConfiguredNodesOnEndpointMethodRequestModel));
                    CloudToDeviceMethodResult methodResult;
                    if (string.IsNullOrEmpty(_publisherModuleName))
                    {
                        methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _getConfiguredNodesOnEndpointMethod, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _getConfiguredNodesOnEndpointMethod, ct).ConfigureAwait(false);
                    }
                    if (!ct.IsCancellationRequested)
                    {
                        if (methodResult.Status == (int)HttpStatusCode.OK)
                        {
                            response = JsonConvert.DeserializeObject<GetConfiguredNodesOnEndpointMethodResponseModel>(methodResult.GetPayloadAsJson());
                            if (response != null && response.OpcNodes != null)
                            {
                                nodes = response.OpcNodes;
                            }
                            if (response == null || response.ContinuationToken == null)
                            {
                                break;
                            }
                            continuationToken = response.ContinuationToken;
                        }
                        else
                        {
                            LogMethodResult(methodResult, _getConfiguredNodesOnEndpointMethod.MethodName);
                            break; ;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Exception");
            }
            return nodes;
        }

        public async Task<bool> UnpublishAllConfiguredNodesAsync(CancellationToken ct)
        {
            CloudToDeviceMethodResult methodResult = null;
            bool result = false;
            try
            {
                UnpublishAllNodesMethodRequestModel unpublishAllNodesMethodRequestModel = new UnpublishAllNodesMethodRequestModel();
                _unpublishAllNodesMethod.SetPayloadJson(JsonConvert.SerializeObject(unpublishAllNodesMethodRequestModel));
                if (string.IsNullOrEmpty(_publisherModuleName))
                {
                    methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _unpublishAllNodesMethod, ct).ConfigureAwait(false);
                }
                else
                {
                    methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _unpublishAllNodesMethod, ct).ConfigureAwait(false);
                }
                List<string> statusResponse = new List<string>();
                if (!ct.IsCancellationRequested)
                {
                    LogMethodResult(methodResult, _publishNodesMethod.MethodName);
                    result = methodResult.Status == (int)HttpStatusCode.OK;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Exception");
            }
            return result;
        }

        /// <summary>
        /// Call the GetInfo method.
        /// </summary>
        public async Task<GetInfoMethodResponseModel> GetInfoAsync(CancellationToken ct)
        {
            GetInfoMethodResponseModel response = null;

            try
            {
                CloudToDeviceMethodResult methodResult = new CloudToDeviceMethodResult();
                if (string.IsNullOrEmpty(_publisherModuleName))
                {
                    methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _getInfoMethod, ct).ConfigureAwait(false);
                }
                else
                {
                    methodResult = await _iotHubClient.InvokeDeviceMethodAsync(_publisherDeviceName, _publisherModuleName, _getInfoMethod, ct).ConfigureAwait(false);
                }

                if (!ct.IsCancellationRequested)
                {
                    if (methodResult.Status == (int)HttpStatusCode.OK)
                    {
                        response = JsonConvert.DeserializeObject<GetInfoMethodResponseModel>(methodResult.GetPayloadAsJson());
                    }
                    else
                    {
                        LogMethodResult(methodResult, _getConfiguredNodesOnEndpointMethod.MethodName);
                    }
                }
            }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested)
                {
                    Logger.Debug(e, $"Exception");
                }
            }

            if (response == null && !ct.IsCancellationRequested)
            {
                Logger.Information("");
                Logger.Information($"OPC Publisher is not responding. Either the used version is too old or it is not running.");
                Logger.Information("");
            }

            return response;
        }

        private void LogMethodResult(CloudToDeviceMethodResult methodResult, string methodName)
        {
            List<string> statusResponse = JsonConvert.DeserializeObject<List<string>>(methodResult.GetPayloadAsJson());
            if (methodResult.Status == (int)HttpStatusCode.OK)
            {
                Logger.Debug($"{methodName} succeeded, status: {((HttpStatusCode)methodResult.Status).ToString()}");
                Logger.Verbose($"Messages returned:");
                foreach (var statusMessage in statusResponse)
                {
                    Logger.Verbose($"{statusMessage}");
                }
            }
            else
            {
                Logger.Error($"{methodName} failed, status: {((HttpStatusCode)methodResult.Status).ToString()}");
                Logger.Error($"Messages returned:");
                foreach (var statusMessage in statusResponse)
                {
                    Logger.Error($"{statusMessage}");
                }
            }
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
        CloudToDeviceMethod _getInfoMethod;
    }
}

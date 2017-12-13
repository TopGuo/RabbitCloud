﻿using Grpc.Core;
using Rabbit.Cloud.Abstractions;
using Rabbit.Cloud.Application.Abstractions;
using Rabbit.Cloud.Application.Features;
using Rabbit.Cloud.Client.Grpc.Features;
using Rabbit.Cloud.Grpc.Abstractions;
using Rabbit.Cloud.Grpc.Abstractions.Client;
using Rabbit.Cloud.Grpc.Abstractions.Utilities.Extensions;
using Rabbit.Cloud.Grpc.Utilities;
using System;
using System.Threading.Tasks;

namespace Rabbit.Cloud.Client.Grpc
{
    public class GrpcMiddleware
    {
        private readonly RabbitRequestDelegate _next;
        private readonly ICallInvokerFactory _callInvokerFactory;
        private readonly IMethodTable _methodTable;

        public GrpcMiddleware(RabbitRequestDelegate next, ICallInvokerFactory callInvokerFactory, IMethodTableProvider methodTableProvider)
        {
            _next = next;
            _callInvokerFactory = callInvokerFactory;
            _methodTable = methodTableProvider.MethodTable;
        }

        public async Task Invoke(IRabbitContext context)
        {
            var grpcRequestFeature = context.Features.Get<IGrpcRequestFeature>();

            if (grpcRequestFeature == null)
                throw new ArgumentNullException(nameof(grpcRequestFeature));

            var requestFeature = context.Features.Get<IRequestFeature>();
            var serviceUrl = requestFeature.ServiceUrl;

            if (serviceUrl == null)
                throw new ArgumentNullException(nameof(requestFeature.ServiceUrl));

            var serviceId = serviceUrl.Path;
            var method = _methodTable.Get(serviceId);

            if (method == null)
                throw new RabbitRpcException(RabbitRpcExceptionCode.Forbidden, $"Can not find service '{serviceId}'.");

            var callInvoker = await _callInvokerFactory.GetCallInvokerAsync(serviceUrl.Host, serviceUrl.Port, requestFeature.ConnectionTimeout);
            // set readTimeout
            grpcRequestFeature.CallOptions = grpcRequestFeature
                .CallOptions
                .WithDeadline(DateTime.UtcNow.Add(requestFeature.ReadTimeout));
            try
            {
                var response = callInvoker.Call(method, grpcRequestFeature.Host, grpcRequestFeature.CallOptions, grpcRequestFeature.Request);

                context.Features.Get<IGrpcResponseFeature>().Response = response;

                //todo: await result, may trigger exception.
                await FluentUtilities.WrapperCallResuleToTask(response);
            }
            catch (RpcException rpcException)
            {
                throw rpcException.WrapRabbitRpcException();
            }

            await _next(context);
        }
    }
}
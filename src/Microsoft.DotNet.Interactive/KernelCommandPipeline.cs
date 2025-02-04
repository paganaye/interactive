// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive.Commands;

namespace Microsoft.DotNet.Interactive;

internal class KernelCommandPipeline
{
    private readonly Kernel _kernel;

    private readonly List<(KernelCommandPipelineMiddleware func, string name)> _middlewares = new();

    private KernelCommandPipelineMiddleware _pipeline;

    public KernelCommandPipeline(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    private void EnsureMiddlewarePipelineIsInitialized()
    {
        _pipeline ??= BuildPipeline();
    }

    internal async Task SendAsync(
        KernelCommand command,
        KernelInvocationContext context)
    {
        command.RoutingSlip.StampAsArrived(_kernel.KernelInfo.Uri);
        
        EnsureMiddlewarePipelineIsInitialized();
            
        try
        {
            await _pipeline(command, context, (_, _) => Task.CompletedTask);
        }
        catch (Exception exception)
        {
            context.Fail(command, exception);
        }
        finally
        {
            command.RoutingSlip.Stamp(_kernel.KernelInfo.Uri);
        }
    }

    [DebuggerHidden]
    private KernelCommandPipelineMiddleware BuildPipeline()
    {
        var invocations = new List<(KernelCommandPipelineMiddleware func, string name)>(_middlewares);

        invocations.Add(
            (
                func: async (command, context, _) => await _kernel.HandleAsync(command, context),
                name: $"HandleAsync({_kernel.Name})"
            ));

        var combined =
            invocations
                .Aggregate(
                    (first, second) =>
                    {
                        return (Combine, first.name + "->" + second.name);

                        async Task Combine(KernelCommand cmd1, KernelInvocationContext ctx1, KernelPipelineContinuation next)
                        {
                            await first.func(cmd1, ctx1, async (cmd2, ctx2) =>
                            {
                                await second.func(cmd2, ctx2, next);
                            });
                        }
                    })
                .func;

        return combined;
    }

    public void AddMiddleware(
        KernelCommandPipelineMiddleware middleware,
        string caller)
    {
        _middlewares.Add((middleware, caller));
        _pipeline = null;
    }
}
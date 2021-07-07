// This file is part of the DSharpPlus project.
//
// Copyright (c) 2015 Mike Santiago
// Copyright (c) 2016-2021 DSharpPlus Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace DSharpPlus.Interactivity.EventHandling
{
    internal class ComponentPaginator : IPaginator, IDisposable
    {
        private readonly DiscordClient _client;
        private readonly InteractivityConfiguration _config;
        private readonly DiscordMessageBuilder _builder = new();
        private readonly Dictionary<ulong, IPaginationRequest> _requests = new();

        public ComponentPaginator(DiscordClient client, InteractivityConfiguration config)
        {
            this._client = client;
            this._client.ComponentInteractionCreated += this.Handle;
            this._config = config;
        }

        public async Task DoPaginationAsync(IPaginationRequest request)
        {
            if (request is not ButtonPaginationRequest breq)
                throw new ArgumentException($"Only {typeof(ButtonPaginationRequest).Name} is supported.", nameof(request));

            var id = (await request.GetMessageAsync().ConfigureAwait(false)).Id;
            this._requests.Add(id, breq);

            try
            {
                var tcs = await request.GetTaskCompletionSourceAsync().ConfigureAwait(false);
                Console.WriteLine("awaiting tcs.Task");
                await tcs.Task;
            }
            catch (Exception ex)
            {
                this._client.Logger.LogError(InteractivityEvents.InteractivityPaginationError, ex, "There was an exception while paginating.");
            }
            finally
            {
                this._requests.Remove(id);
                Console.WriteLine("Removed request");
                try
                {
                    Console.WriteLine("Cleaning up");
                    await request.DoCleanupAsync().ConfigureAwait(false);
                    Console.WriteLine("Cleaned up");
                }
                catch (Exception e)
                {
                    this._client.Logger.LogError(InteractivityEvents.InteractivityPaginationError, e, "There was an exception while cleaning up pagination.");
                }
            }
        }


        private async Task Handle(DiscordClient _, ComponentInteractionCreateEventArgs e)
        {
            if (!this._requests.TryGetValue(e.Message.Id, out var req))
                return;

            if (await req.GetUserAsync().ConfigureAwait(false) != e.User)
                return;

            if (this._config.ACKPaginationButtons)
            {
                e.Handled = true;
                await e.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate);
            }
            await this.HandlePaginationAsync(req, e).ConfigureAwait(false);
        }
        private async Task HandlePaginationAsync(IPaginationRequest request, ComponentInteractionCreateEventArgs args)
        {
            var buttons = this._config.PaginationButtons;
            var msg = await request.GetMessageAsync().ConfigureAwait(false);
            var id = args.Id;
            var tcs = await request.GetTaskCompletionSourceAsync().ConfigureAwait(false);

            var paginationTask = id switch
            {
                _ when id == buttons.SkipLeft.CustomId => request.SkipLeftAsync(),
                _ when id == buttons.SkipRight.CustomId => request.SkipRightAsync(),
                _ when id == buttons.Stop.CustomId => StopAsync(),
                _ when id == buttons.Left.CustomId => request.PreviousPageAsync(),
                _ when id == buttons.Right.CustomId => request.NextPageAsync(),
            };

            await paginationTask.ConfigureAwait(false);

            if (id == buttons.Stop.CustomId)
                return;

            var page = await request.GetPageAsync().ConfigureAwait(false);

            this._builder.Clear();

            this._builder
                .WithContent(page.Content)
                .AddEmbed(page.Embed)
                .AddComponents(await request.GetButtonsAsync().ConfigureAwait(false));

            await this._builder.ModifyAsync(msg);

            Task StopAsync() => Task.FromResult(tcs.TrySetResult(true));

        }

        public void Dispose()
        {
            this._requests.Clear();
            this._client.ComponentInteractionCreated -= this.Handle;
        }
    }
}
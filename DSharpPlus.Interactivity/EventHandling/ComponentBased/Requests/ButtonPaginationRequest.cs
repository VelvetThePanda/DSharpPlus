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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Enums;

namespace DSharpPlus.Interactivity.EventHandling
{
    internal class ButtonPaginationRequest : IPaginationRequest
    {
        private int _index;
        private readonly List<Page> _pages = new();

        private readonly TaskCompletionSource<bool> _tcs = new();

        private readonly CancellationToken _token;
        private readonly DiscordUser _user;
        private readonly DiscordMessage _message;
        private readonly PaginationButtons _buttons;
        private readonly PaginationBehaviour _wrapBehavior;
        private readonly ButtonPaginationBehavior _behaviorBehavior;

        public ButtonPaginationRequest(DiscordMessage message, DiscordUser user,
            PaginationBehaviour behavior, ButtonPaginationBehavior behaviorBehavior,
            PaginationButtons buttons, Page[] pages, CancellationToken token)
        {
            this._user = user;
            this._token = token;
            this._buttons = new(buttons);
            this._message = message;
            this._wrapBehavior = behavior;
            this._behaviorBehavior = behaviorBehavior;
            this._pages.AddRange(pages);

            this._token.Register(() => this._tcs.TrySetResult(false));
        }

        public int PageCount => this._pages.Count;

        public Task<Page> GetPageAsync() => Task.FromResult(this._pages[this._index]);
        public Task SkipLeftAsync()
        {
            if (this._wrapBehavior is PaginationBehaviour.WrapAround)
            {
                this._index = this._index is 0 ? this._pages.Count - 1 : 0;
                return Task.CompletedTask;
            }

            this._index = 0;
            this._buttons.Left.Disable();
            this._buttons.SkipLeft.Disable();

            this._buttons.Right.Enable();
            this._buttons.SkipRight.Enable();

            return Task.CompletedTask;
        }
        public Task SkipRightAsync()
        {
            if (this._wrapBehavior is PaginationBehaviour.WrapAround)
            {
                this._index = this._index is 0 ? this.PageCount - 1 : 0;
                return Task.CompletedTask;
            }

            this._index = this._pages.Count - 1;

            this._buttons.Left.Enable();
            this._buttons.SkipLeft.Enable();

            this._buttons.Right.Disable();
            this._buttons.SkipRight.Disable();

            return Task.CompletedTask;
        }
        public Task NextPageAsync()
        {
            this._index++;

            if (this._wrapBehavior is PaginationBehaviour.WrapAround)
            {
                if (this._index >= this.PageCount)
                    this._index = 0;

                return Task.CompletedTask;
            }

            if (this._index > this._pages.Count - 2)
                this._buttons.SkipRight.Disable();

            if (this._index == this._pages.Count - 1)
                this._buttons.Right.Disable();

            return Task.CompletedTask;
        }
        public Task PreviousPageAsync()
        {
            this._index--;

            if (this._wrapBehavior is PaginationBehaviour.WrapAround)
            {
                if (this._index is - 1)
                    this._index = this._pages.Count - 1;

                return Task.CompletedTask;
            }

            if (this._index is 1)
                this._buttons.SkipLeft.Disable();

            if (this._index is 0)
                this._buttons.Left.Disable();

            this._buttons.Right.Enable();

            return Task.CompletedTask;
        }
        public Task<PaginationEmojis> GetEmojisAsync()
            => Task.FromException<PaginationEmojis>(new NotSupportedException("Emojis aren't supported for this request."));

        public Task<IEnumerable<DiscordButtonComponent>> GetButtonsAsync()
            => Task.FromResult(this._message.Components.First().Components.Cast<DiscordButtonComponent>());

        public Task<DiscordMessage> GetMessageAsync() => Task.FromResult(this._message);
        public Task<DiscordUser> GetUserAsync() => Task.FromResult(this._user);
        public Task<TaskCompletionSource<bool>> GetTaskCompletionSourceAsync() => Task.FromResult(this._tcs);

        // This is essentially the stop method. //
        public async Task DoCleanupAsync()
        {
            switch (this._behaviorBehavior)
            {
                case ButtonPaginationBehavior.Disable:
                    var buttons = (await this.GetButtonsAsync()).Select(b => b.Disable());

                    var builder = new DiscordMessageBuilder()
                        .WithContent(this._message.Content)
                        .AddEmbeds(this._message.Embeds)
                        .AddComponents(buttons);

                    await builder.ModifyAsync(this._message);
                    break;

                case ButtonPaginationBehavior.DeleteMessage:
                    await this._message.DeleteAsync();
                    break;

                case ButtonPaginationBehavior.Ignore:
                    break;
            }
            this._tcs.TrySetResult(true);
        }
    }
}
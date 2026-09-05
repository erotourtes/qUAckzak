using System.Collections.Generic;
using DuckGame;
using Microsoft.Xna.Framework;

namespace qUAckzak.Mod.Modes
{
    internal sealed class ModeHost : GameComponent
    {
        private readonly IReadOnlyList<IModMode> _modes;
        private Level _currentLevel;

        public ModeHost(Game game, params IModMode[] modes)
            : base(game)
        {
            _modes = modes;
        }

        public override void Update(GameTime gameTime)
        {
            if (_currentLevel != Level.current)
            {
                foreach (IModMode mode in _modes)
                {
                    mode.Reset();
                }

                _currentLevel = Level.current;
            }

            foreach (IModMode mode in _modes)
            {
                mode.Update();
            }
        }
    }
}

using Rewired;
using UnityEngine;

namespace MedusaMultiCrew.Core
{
    // Nuclear Option reads all input through Rewired; UnityEngine.Input does not work in this
    // build, so keys must be polled through ReInput. Adapted from SimpleWSO (CC0, Bongus/Motmir).
    internal static class RewiredInput
    {
        public static bool Ready => ReInput.isReady && ReInput.controllers != null;

        public static bool GetKeyDown(KeyCode key)
        {
            if (!Ready)
            {
                return false;
            }
            if (TryMouseButton(key, out int button))
            {
                Mouse mouse = ReInput.controllers.Mouse;
                return mouse != null && mouse.GetButtonDown(button);
            }
            Keyboard keyboard = ReInput.controllers.Keyboard;
            return keyboard != null && keyboard.GetKeyDown(key);
        }

        // Vanilla actions ("Fire", "Next Weapon", ...), polled the way PilotPlayerState does.
        public static bool GetAction(string action)
        {
            return Ready && GameManager.playerInput != null && GameManager.playerInput.GetButton(action);
        }

        public static bool GetActionTimedPressUp(string action)
        {
            return Ready && GameManager.playerInput != null && GameManager.playerInput.GetButtonTimedPressUp(action, 0f, PlayerSettings.clickDelay);
        }

        private static bool TryMouseButton(KeyCode key, out int button)
        {
            switch (key)
            {
                case KeyCode.Mouse0: button = 0; return true;
                case KeyCode.Mouse1: button = 1; return true;
                case KeyCode.Mouse2: button = 2; return true;
                case KeyCode.Mouse3: button = 3; return true;
                case KeyCode.Mouse4: button = 4; return true;
                default: button = -1; return false;
            }
        }
    }
}

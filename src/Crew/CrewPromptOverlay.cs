using UnityEngine;

namespace MedusaMultiCrew.Crew
{
    // On-screen boxes the player can't miss: the pilot's "X wants to join" prompt (keys,
    // clickable buttons, countdown, pulsing border) and the joining player's "waiting for the
    // pilot" box. Drawn with IMGUI from Plugin.OnGUI, scaled from a 1080p layout.
    internal static class CrewPromptOverlay
    {
        private const float ReferenceHeight = 1080f;
        private const float Width = 560f;

        private static GUIStyle title;
        private static GUIStyle body;
        private static GUIStyle button;
        private static Texture2D white;

        public static void OnGUI()
        {
            if (!CrewClient.HasPrompt && !CrewClient.IsWaitingForPilot)
            {
                return;
            }
            EnsureStyles();

            float scale = Screen.height / ReferenceHeight;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float centerX = Screen.width / scale / 2f;

            if (CrewClient.HasPrompt)
            {
                DrawPrompt(new Rect(centerX - Width / 2f, 90f, Width, 170f));
            }
            else
            {
                DrawWaiting(new Rect(centerX - Width / 2f, 90f, Width, 90f));
            }
            GUI.matrix = previous;
        }

        private static void DrawPrompt(Rect rect)
        {
            // Pulse the border so it catches the eye mid-fight.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            DrawPanel(rect, Color.Lerp(new Color(1f, 0.55f, 0f), new Color(1f, 0.9f, 0.3f), pulse));

            string who = string.IsNullOrEmpty(CrewClient.PromptRequester) ? "A player" : CrewClient.PromptRequester;
            GUI.Label(new Rect(rect.x, rect.y + 14f, rect.width, 34f), "CREW REQUEST", title);
            GUI.Label(new Rect(rect.x + 16f, rect.y + 50f, rect.width - 32f, 30f),
                $"{who} wants to join as {CrewSeat.Name(CrewClient.PromptSeat)}", body);
            GUI.Label(new Rect(rect.x + 16f, rect.y + 80f, rect.width - 32f, 26f),
                $"{Plugin.AcceptKey.Value} = accept    {Plugin.DeclineKey.Value} = decline    ({Mathf.CeilToInt(CrewClient.PromptSecondsLeft)} s)", body);

            // Clickable too, for when the cursor is out (map open).
            float buttonWidth = 160f;
            float buttonY = rect.y + rect.height - 52f;
            if (GUI.Button(new Rect(rect.center.x - buttonWidth - 10f, buttonY, buttonWidth, 38f), "Accept", button))
            {
                CrewClient.AnswerPrompt(true);
            }
            if (GUI.Button(new Rect(rect.center.x + 10f, buttonY, buttonWidth, 38f), "Decline", button))
            {
                CrewClient.AnswerPrompt(false);
            }
        }

        private static void DrawWaiting(Rect rect)
        {
            DrawPanel(rect, new Color(0.45f, 0.75f, 1f));
            GUI.Label(new Rect(rect.x, rect.y + 12f, rect.width, 34f), "WAITING FOR THE PILOT", title);
            GUI.Label(new Rect(rect.x + 16f, rect.y + 48f, rect.width - 32f, 30f),
                $"Your crew request was sent ({Mathf.CeilToInt(CrewClient.WaitingSecondsLeft)} s)", body);
        }

        private static void DrawPanel(Rect rect, Color border)
        {
            const float thickness = 4f;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(rect, white);
            GUI.color = border;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), white);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), white);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), white);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), white);
            GUI.color = previous;
        }

        private static void EnsureStyles()
        {
            if (white != null && title != null)
            {
                return;
            }
            white = Texture2D.whiteTexture;
            title = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.85f, 0.3f) },
            };
            body = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                normal = { textColor = Color.white },
            };
            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
            };
        }
    }
}

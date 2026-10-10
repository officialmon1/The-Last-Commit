using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Panda
{
    /// <summary>One global click listener: raycasts into the 3D scene for blocks/the panda, ignoring clicks on UI.</summary>
    public class ClickRouter : MonoBehaviour
    {
        public Camera worldCamera;

        void Awake()
        {
            if (worldCamera == null) worldCamera = Camera.main;
        }

        void Update()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return; // a UI button handled it

            if (worldCamera == null) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Ray ray = worldCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                var block = hit.collider.GetComponentInParent<ProblemBlock>();
                if (block != null) { block.OnClicked(); return; }

                var panda = hit.collider.GetComponentInParent<PandaCompanion>();
                if (panda != null) panda.OnClicked();
            }
        }
    }
}

using Banter.SDK;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TLab.WebView
{
    public class BrowserInputListener : BaseInputListener
    {
        [SerializeField] public Browser browser;
        private long m_downTime;

        private string THIS_NAME => "[" + this.GetType() + "] ";

        public enum TouchEvent
        {
            Down,
            Up,
            Drag,
            Move
        };

        protected override void OnPointerUp(PointerEventData pointerEventData, InputEventData inputEventData)
        {
            var position = inputEventData.position;
            position.x *= browser.viewSize.x;
            position.y *= browser.viewSize.y;
            browser.TouchEvent((int)position.x, (int)position.y, (int)TouchEvent.Up, m_downTime);
        }

        protected override void OnPointerExit(PointerEventData pointerEventData, InputEventData inputEventData)
        {
            var position = inputEventData.position;
            position.x *= browser.viewSize.x;
            position.y *= browser.viewSize.y;
            browser.TouchEvent((int)position.x, (int)position.y, (int)TouchEvent.Up, m_downTime);
        }

        protected override void OnPointerDown(PointerEventData pointerEventData, InputEventData inputEventData)
        {
            var position = inputEventData.position;
            position.x *= browser.viewSize.x;
            position.y *= browser.viewSize.y;
            m_downTime = browser.TouchEvent((int)position.x, (int)position.y, (int)TouchEvent.Down, m_downTime);
        }

        protected override void OnDrag(PointerEventData pointerEventData, InputEventData inputEventData)
        {
            var position = inputEventData.position;
            position.x *= browser.viewSize.x;
            position.y *= browser.viewSize.y;
            browser.TouchEvent((int)position.x, (int)position.y, (int)TouchEvent.Drag, m_downTime);
        }

        protected override void OnPointerMove(PointerEventData pointerEventData, InputEventData inputEventData)
        {
            var position = inputEventData.position;
            position.x *= browser.viewSize.x;
            position.y *= browser.viewSize.y;
            browser.TouchEvent((int)position.x, (int)position.y, (int)TouchEvent.Move, m_downTime);
        }
    }
}
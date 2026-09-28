using NUnit.Framework;
using Safehouse.UI;
using UnityEngine;

namespace Safehouse.Tests
{
    public sealed class RouteMapTests
    {
        [Test]
        public void NodeCentersStayInsideTheMargin()
        {
            var origin = RouteMapLayout.Center(0f, 0f, 1000f, 800f);
            var far = RouteMapLayout.Center(1f, 1f, 1000f, 800f);
            Assert.AreEqual(new Vector2(RouteMapLayout.Margin, RouteMapLayout.Margin), origin);
            Assert.AreEqual(new Vector2(1000f - RouteMapLayout.Margin, 800f - RouteMapLayout.Margin), far);
            Assert.Greater(far.x, origin.x);
            Assert.Greater(far.y, origin.y);
        }

        [Test]
        public void ANarrowMapStillPlacesTheCenterBetweenTheEdges()
        {
            var center = RouteMapLayout.Center(0.5f, 0.25f, 80f, 40f);
            Assert.Greater(center.x, 0f);
            Assert.Less(center.x, 80f);
            Assert.Greater(center.y, 0f);
            Assert.Less(center.y, 40f);
        }
    }
}

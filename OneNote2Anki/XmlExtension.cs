using System.Xml;
using System.Xml.Linq;

namespace ExtensionMethods {
    public static class XmlExtension {
        public enum CopyMode {
            Front,
            Back
        }

        public static void InspectElement(this XElement element, int level = 0) {
            var inset = new string('\t', level);
            var tag = element.Name.LocalName;
            var attribs = string.Join(" ", element.Attributes());
            Console.WriteLine("{0}<{1}{2}>", inset, tag, attribs.Length == 0 ? "" : " " + attribs);
            foreach (var node in element.Nodes()) {
                if (node.NodeType == XmlNodeType.Element) {
                    InspectElement((XElement)node, level + 1);
                } else {
                    Console.WriteLine("{0}\t'{1}'", inset, node);
                }
            }
            Console.WriteLine("{0}</{1}>", inset, tag);
        }

        public static XElement CopyAncestors(this XNode node, XElement topElement, CopyMode mode) {
            var text = ((XText)node).Value;
            var index = text.IndexOf(':');
            if (mode == CopyMode.Front) text = text.Substring(0, index);
            else text = text.Substring(index + 1);

            var textNode = new XText(text) as XNode;
            XElement? last = null;

            var ancestors = new List<XElement>();
            foreach (var ancestor in node.Ancestors()) {
                ancestors.Add(ancestor);
                if (ancestor == topElement) break;
            }

            var element = (XElement)ancestors.Aggregate(textNode, (acc, cur) => {
                var elem = mode == CopyMode.Front
                    ? new XElement(cur.Name.LocalName, cur.Attributes(), (last ?? node).NodesBeforeSelf(), acc)
                    : new XElement(cur.Name.LocalName, cur.Attributes(), acc, (last ?? node).NodesAfterSelf());
                last = cur;
                return elem;
            });

            return element;
        }

        public static (XElement, XElement)? SplitElement(this XElement element, XElement? topElement = null) {
            if (topElement == null)
                topElement = element;

            foreach (var node in element.Nodes()) {
                (XElement, XElement)? result = null;
                switch (node.NodeType) {
                    case XmlNodeType.Element:
                        result = SplitElement((XElement)node, topElement);
                        break;
                    case XmlNodeType.Text:
                        var text = ((XText)node).Value;
                        if (text.Contains(':')) {
                            var front = CopyAncestors(node, topElement, CopyMode.Front);
                            var back = CopyAncestors(node, topElement, CopyMode.Back);
                            return (front, back);
                        }
                        break;
                    default:
                        throw new Exception($"Unsupported node type {node.NodeType}.");
                }
                if (result != null) return result;
            }
            return null;
        }

        public static XmlReader GetReader(this XElement element) {
            var reader = element.CreateReader();
            reader.MoveToContent();
            return reader;
        }
    }
}
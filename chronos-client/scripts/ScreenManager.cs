using Godot;

#nullable enable

public static class ScreenManager
{
	private static readonly System.Collections.Generic.Dictionary<SceneTree, System.Collections.Generic.Stack<Node>>
		Stacks = new();

	private sealed class NodeState
	{
		public readonly bool HasCanvasItem;
		public readonly bool Visible;
		public readonly bool Process;
		public readonly bool PhysicsProcess;

		public NodeState(Node node)
		{
			if (node is CanvasItem ci)
			{
				HasCanvasItem = true;
				Visible = ci.Visible;
			}
			else
			{
				HasCanvasItem = false;
				Visible = true;
			}

			Process = node.IsProcessing();
			PhysicsProcess = node.IsPhysicsProcessing();
		}
	}

	private static readonly System.Collections.Generic.Dictionary<Node, NodeState> HiddenStates = new();

	private static System.Collections.Generic.Stack<Node> GetStack(SceneTree tree)
	{
		if (!Stacks.TryGetValue(tree, out var stack))
		{
			stack = new System.Collections.Generic.Stack<Node>();
			Stacks[tree] = stack;
		}

		// Clean top invalid nodes
		while (stack.Count > 0 && !IsNodeValid(stack.Peek()))
			stack.Pop();

		return stack;
	}

	private static bool IsNodeValid(Node node)
		=> node is not null && GodotObject.IsInstanceValid(node);

	private static void HideCurrent(Node current)
	{
		if (!IsNodeValid(current))
			return;

		if (HiddenStates.ContainsKey(current))
			return;

		HiddenStates[current] = new NodeState(current);

		if (current is CanvasItem ci)
			ci.Visible = false;
		current.SetProcess(false);
		current.SetPhysicsProcess(false);
	}

	private static void RestorePrevious(Node node)
	{
		if (!IsNodeValid(node))
			return;

		if (!HiddenStates.TryGetValue(node, out var st))
			return;

		if (node is CanvasItem ci && st.HasCanvasItem)
			ci.Visible = st.Visible;
		node.SetProcess(st.Process);
		node.SetPhysicsProcess(st.PhysicsProcess);

		HiddenStates.Remove(node);
	}

	public static void Change(Node current, Node next)
	{
		var tree = current.GetTree();
		var root = tree.Root;

		var stack = GetStack(tree);
		while (stack.Count > 0)
		{
			var n = stack.Pop();
			if (IsNodeValid(n))
			{
				HiddenStates.Remove(n);
				n.QueueFree();
			}
		}

		root.AddChild(next);
		stack.Push(next);

		current.QueueFree();
	}

	/// <summary>
	/// Push màn hình mới lên trên. Màn hình current được ẩn đi.
	/// </summary>
	public static void Push(Node current, Node next)
	{
		if (!IsNodeValid(next))
			return;

		var tree = current.GetTree();
		var root = tree.Root;

		var stack = GetStack(tree);
		HideCurrent(current);

		root.AddChild(next);
		stack.Push(next);
	}

	public static void Pop(Node current)
	{
		if (!IsNodeValid(current))
			return;

		var tree = current.GetTree();
		var stack = GetStack(tree);

		// Clean invalid top first
		while (stack.Count > 0 && !IsNodeValid(stack.Peek()))
			stack.Pop();

		// Nếu current không phải top (do stack có thể đã bị clear/hủy), ta tìm đến đỉnh hợp lệ.
		while (stack.Count > 0 && stack.Peek() != current)
		{
			var n = stack.Pop();
			if (IsNodeValid(n))
			{
				HiddenStates.Remove(n);
				n.QueueFree();
			}
		}

		// Pop chính current
		if (stack.Count > 0 && stack.Peek() == current)
			stack.Pop();

		HiddenStates.Remove(current);
		current.QueueFree();

		// Show previous
		while (stack.Count > 0 && !IsNodeValid(stack.Peek()))
			stack.Pop();

		if (stack.Count > 0)
		{
			var prev = stack.Peek();
			if (IsNodeValid(prev))
			{
				RestorePrevious(prev);
			}
		}
	}
}


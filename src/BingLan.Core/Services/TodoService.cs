using BingLan.Core.Models;

namespace BingLan.Core.Services;

public static class TodoService
{
    public static TodoWidgetState CreateDefaultWidget()
    {
        var widget = new TodoWidgetState();
        for (var index = 0; index < 3; index++)
        {
            Add(widget);
        }
        return widget;
    }

    public static TodoItemState Add(TodoWidgetState widget, string text = "")
    {
        var item = new TodoItemState
        {
            Text = text,
            Order = widget.Items.Count == 0 ? 0 : widget.Items.Max(x => x.Order) + 1
        };
        widget.Items.Add(item);
        Normalize(widget);
        return item;
    }

    public static bool Remove(TodoWidgetState widget, Guid itemId)
    {
        var removed = widget.Items.RemoveAll(x => x.Id == itemId) > 0;
        if (removed)
        {
            Normalize(widget);
        }
        return removed;
    }

    public static int RemoveCompleted(TodoWidgetState widget)
    {
        var removed = widget.Items.RemoveAll(x => x.IsCompleted);
        if (removed > 0)
        {
            Normalize(widget);
        }
        return removed;
    }

    public static bool Move(TodoWidgetState widget, Guid itemId, int offset)
    {
        var ordered = widget.Items.OrderBy(x => x.Order).ToList();
        var index = ordered.FindIndex(x => x.Id == itemId);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return false;
        }

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }
        widget.Items = ordered;
        return true;
    }

    public static void Normalize(TodoWidgetState widget)
    {
        widget.Items = widget.Items
            .OrderBy(x => x.Order)
            .Select((item, index) =>
            {
                item.Order = index;
                return item;
            })
            .ToList();
    }
}

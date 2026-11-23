namespace CSharpApp.Application.Categories;

/// <summary>Creates a category; no read-after-write, because the upstream stores category fields as sent (ASM-11).</summary>
public sealed record CreateCategoryCommand(string Name, string Image) : ICommand<Category>
{
    /// <summary>Calls the upstream categories client.</summary>
    public sealed class Handler(ICategoriesClient categories) : ICommandHandler<CreateCategoryCommand, Category>
    {
        public ValueTask<Category> Handle(CreateCategoryCommand command, CancellationToken cancellationToken) =>
            new(categories.CreateAsync(new NewCategory(command.Name, command.Image), cancellationToken));
    }
}

# JsonToQueryable

Parse JSON string to expressions that used for querying in EntityFramework.Core

For example:
```js
{
    query: {
        Name: contains 'Default',
        Children: {
            Id: >100,
            Active: true,                    
        },               
        AnotherEntity
    },
    page:2,//starts on 0
    pageSize:25
}
```
will be convert to
```csharp
dbContext.Parent.Include(p.Children)
                .Include(p.AnotherEntity)
                .Where(p=>p.Name.Contains("Default") && p.Children.Any(c=>c.Id > 100 && c.Active))
                .Skip(page * pageSize)
                .Take(pageSize);
```

# JsonToQueryable

Parse JSON string to expressions that used for querying in EntityFramework.Core

For example:
```js
{
    Name: 'Default',
    Children: {
        Id: >100,
        Active: true,                    
    },               
    AnotherEntity
}
```
will be convert to
```csharp
dbContext.Parent.Include(p.Children).Include(p.AnotherEntity).Where(p=>p.Name == "Default" && p.Children.Any(c=>c.Id > 100 && c.Active))
```

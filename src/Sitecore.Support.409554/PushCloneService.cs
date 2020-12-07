using System.Collections.Generic;
using System.Linq;
using Sitecore.Data.Comparers;
using Sitecore.Data.Items;
using Sitecore.Pipelines;
using Sitecore.XA.Foundation.Multisite.Pipelines.PushCloneChanges;
using Sitecore.XA.Foundation.Multisite.Services;

namespace Sitecore.Support.XA.Foundation.Multisite.Services
{
    public class PushCloneService : IPushCloneService
    {
        private readonly IPushCloneCoordinatorService _coordinatorService;

        public PushCloneService(IPushCloneCoordinatorService pushCloneCoordinatorService)
        {
            _coordinatorService = pushCloneCoordinatorService;
        }

        public void AddChild(Item item)
        {
            var parent = item.Parent;

            #region Support Fix 409554
            if (parent == null)
            {
                return;
            }
            #endregion
            var clones = parent.GetClones();

            foreach (var clone in clones)
            {
                if (!_coordinatorService.ShouldProcess(clone))
                {
                    continue;
                }

                if (item.Versions.GetVersionNumbers().Length > 0)
                {
                    var cloneItem = item.CloneTo(clone);
                    ProtectItem(cloneItem);
                }
            }
        }

        public void Move(Item item)
        {
            if (item.Parent.HasClones)
            {
                var parentClones = GetCloneItem(item.Parent);
                foreach (var parentClone in parentClones.ToList())
                {
                    if (!_coordinatorService.ShouldProcess(parentClone))
                    {
                        return;
                    }

                    var clones = GetCloneItem(item);
                    foreach (var clone in clones)
                    {
                        if (parentClone.Paths.FullPath.Contains(clone.Paths.FullPath) || clone.Paths.FullPath.Contains(parentClone.Paths.FullPath))
                        {
                            clone.MoveTo(parentClone);
                        }
                    }
                }
            }
        }

        public void Remove(Item item)
        {
            var clones = item.GetClones();
            foreach (var clone in clones)
            {
                if (_coordinatorService.ShouldProcess(clone))
                {
                    clone.Delete();
                }
            }
        }

        public void SaveClone(Item item, ItemChanges changes)
        {
            var clones = GetCloneItem(item);
            foreach (var clone in clones)
            {
                if (!_coordinatorService.ShouldProcess(clone))
                {
                    return;
                }

                var args = new PushCloneChangesArgs()
                {
                    Item = item,
                    Changes = changes,
                    Clone = clone
                };

                CorePipeline.Run("pushCloneChanges", args);
            }
        }

        public void AddVersion(Item item)
        {
            var parent = item.Parent;
            #region Support Fix 409554

            if (parent == null)
            {
                return;
            }
            if (item.Versions.Count == 0)
            {
                return;
            }
            #endregion
            var latest = item.Versions.GetLatestVersion();
            var versionUri = latest.Uri;
            var clones = GetCloneItem(latest);
            var enumerable = clones as IList<Item> ?? clones.ToList();
            if (!enumerable.Any() && parent.HasClones)
            {
                var parentClones = GetCloneItem(parent);
                foreach (var parentClone in parentClones)
                {
                    if (!_coordinatorService.ShouldProcess(parentClone))
                    {
                        continue;
                    }

                    var cloneItem = item.CloneTo(parentClone);
                    CopyWorkflow(item, cloneItem);
                    ProtectItem(cloneItem);
                }
            }
            else
            {
                foreach (var clone in enumerable)
                {
                    if (!_coordinatorService.ShouldProcess(clone))
                    {
                        continue;
                    }

                    var versionedClone = clone.Database.GetItem(clone.ID, latest.Language);
                    using (new SecurityModel.SecurityDisabler())
                    {
                        var newVersion = versionedClone.Versions.AddVersion();
                        newVersion.Editing.BeginEdit();
                        newVersion[FieldIDs.Source] = versionUri.ToString();
                        newVersion[FieldIDs.SourceItem] = versionUri.ToString(false);
                        newVersion.Editing.EndEdit();
                    }
                }
            }
        }

        protected virtual void CopyWorkflow(Item source, Item target)
        {
            var item = source.Database.GetItem(source.ID);
            target.Editing.BeginEdit();
            target[FieldIDs.Workflow] = item[FieldIDs.Workflow];
            target[FieldIDs.WorkflowState] = item[FieldIDs.WorkflowState];
            target.Editing.EndEdit();
        }

        protected virtual void ProtectItem(Item item)
        {
            item.Editing.BeginEdit();
            item.Appearance.ReadOnly = true;
            item.Editing.EndEdit();
        }

        protected virtual IEnumerable<Item> GetCloneItem(Item item)
        {
            return item.GetClones().Distinct(new ItemIdComparer());
        }

        public void RemoveVersion(Item commandItem)
        {
        }
    }
}
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using _1RM.Controls;
using _1RM.Model;
using _1RM.Model.Protocol.Base;
using _1RM.Utils;
using Shawn.Utils.Interface;
using Shawn.Utils.Wpf;
using Stylet;

namespace _1RM.View.ServerList
{
    public partial class ServerListPageViewModel
    {
        public const string TAB_ALL_NAME = "";
        public const string TAB_TAGS_LIST_NAME = "tags_selector_for_list@#@1__()!";
        public const string TAB_NONE_SELECTED = "@#@$*%&@!_)@#(&*&!@^$(*&@^*&$^1";


        private bool _isTagFiltersShown;
        /// <summary>
        /// a value indicating whether to display the `tag filters indicator bar`.
        /// </summary>
        public bool IsTagFiltersShown
		{
			get => _isTagFiltersShown;
			set => SetAndNotifyIfChanged(ref _isTagFiltersShown, value);
		}

        private string _selectedTabName = TAB_ALL_NAME;
        public string SelectedTabName { get => _selectedTabName; private set => SetAndNotifyIfChanged(ref _selectedTabName, value); }


        private void OnGlobalDataTagListChanged()
        {
            HeaderTags = new BindableCollection<Tag>(AppData.TagList.OrderBy(x => x.CustomOrder).ThenBy(x => x.Name));

            // 当修改了tags后，将无效的tag筛选器移除。
            var needRemove = new List<string>();
            var filters = TagFilters.ToList();
            foreach (var filter in filters)
            {
                if (AppData.TagList.All(x => x.Name != filter.TagName))
                {
                    needRemove.Add(filter.TagName);
                }
            }

            if (!needRemove.Any()) return;
            foreach (var tag in needRemove)
            {
                FilterTagsControl(tag, TagFilter.FilterTagsControlAction.Remove);
            }
        }

        public void CalcTagFilterBarVisibility()
        {
            if (_tagFilters.Count == 1
                && _tagFilters[0].IsIncluded == true
                && AppData.TagList.Any(tag => tag.IsPinned && tag.Name == _tagFilters[0].TagName))
            {
                // If the current tag is `IsIncluded` and already pinned on top, then do not display the tag selector indicator.
                IsTagFiltersShown = false;
            }
            else if (_tagFilters.Count > 0)
            {
                IsTagFiltersShown = true;
            }
            else
            {
                IsTagFiltersShown = false;
            }
        }

        private List<TagFilter> _tagFilters = new List<TagFilter>();
        public List<TagFilter> TagFilters
        {
            get => _tagFilters;
            set
            {
                if (SetAndNotifyIfChanged(ref _tagFilters, value))
                {
                    string tagName;
                    if (_tagFilters.Count == 1 && _tagFilters.First().IsIncluded)
                    {
                        tagName = _tagFilters.First().TagName;
                    }
                    else if (_tagFilters.Count == 0)
                    {
                        tagName = TAB_ALL_NAME;
                    }
                    else
                    {
                        tagName = TAB_NONE_SELECTED;
                    }

                    CalcTagFilterBarVisibility();

                    if (SelectedTabName == tagName) return;
                    SelectedTabName = tagName;
                }
            }
        }


        #region Tag filter control

        /// <summary>
        /// 控制 tag 选择器，新增或删除 tag 过滤器。
        /// </summary>
        /// <param name="o"></param>
        /// <param name="action"></param>
        private void FilterTagsControl(object? o, TagFilter.FilterTagsControlAction action)
        {
            string newTagName;
            {
                if (o is Tag obj
                    && (AppData.TagList.Any(x => x.Name == obj.Name) || action == TagFilter.FilterTagsControlAction.Remove))
                {
                    newTagName = obj.Name;
                }
                else if (o is string str
                         && (AppData.TagList.Any(x => x.Name == str) || action == TagFilter.FilterTagsControlAction.Remove))
                {
                    newTagName = str;
                }
                else
                {
                    return;
                }
            }

            TagListViewModel = null;

            if (string.IsNullOrEmpty(newTagName) == false)
            {
                var filters = TagFilters.ToList();
                var existed = filters.FirstOrDefault(x => x.TagName == newTagName);
                // remove action
                if (action == TagFilter.FilterTagsControlAction.Remove)
                {
                    if (existed != null)
                    {
                        filters.Remove(existed);
                    }
                }
                // append action
                else if (action == TagFilter.FilterTagsControlAction.AppendIncludedFilter
                         || action == TagFilter.FilterTagsControlAction.AppendExcludedFilter)
                {
                    bool isExcluded = action == TagFilter.FilterTagsControlAction.AppendExcludedFilter;
                    if (existed == null)
                    {
                        filters.Add(TagFilter.Create(newTagName, isExcluded ? TagFilter.FilterType.Excluded : TagFilter.FilterType.Included));
                    }
                    if (existed != null && existed.IsExcluded != isExcluded)
                    {
                        filters.Remove(existed);
                        filters.Add(TagFilter.Create(newTagName, isExcluded ? TagFilter.FilterType.Excluded : TagFilter.FilterType.Included));
                    }
                }
                // set action
                else
                {
                    filters.Clear();
                    filters.Add(TagFilter.Create(newTagName, TagFilter.FilterType.Included));
                }

                TagFilters = filters;
                IoC.Get<MainWindowViewModel>().SetMainFilterString(TagFilters, TagAndKeywordEncodeHelper.DecodeKeyword(IoC.Get<MainWindowViewModel>().MainFilterString).KeyWords);
            }
        }

        private RelayCommand? _cmdShowMainTab;
        public RelayCommand CmdShowMainTab
        {
            get
            {
                return _cmdShowMainTab ??= new RelayCommand((o) =>
                {
                    if(TagFilters.Any()) TagFilters = new List<TagFilter>();
                    TagListViewModel = null;
                    SelectedTabName = TAB_ALL_NAME;
                    IoC.Get<MainWindowViewModel>().SetMainFilterString(null, null);
                });
            }
        }

        private RelayCommand? _cmdShowTagTab;
        public RelayCommand CmdShowTagTab
        {
            get
            {
                return _cmdShowTagTab ??= new RelayCommand((o) =>
                {
                    IoC.Get<MainWindowViewModel>().SetMainFilterString(null, null);
                    TagListViewModel = TagsPanelViewModel;
                    SelectedTabName = TAB_ALL_NAME;
                });
            }
        }


        private RelayCommand? _cmdTagAddIncluded;
        public RelayCommand CmdTagAddIncluded
        {
            get
            {
                return _cmdTagAddIncluded ??= new RelayCommand((o) =>
                {
                    var isCtrlDown = (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl));
                    FilterTagsControl(o, isCtrlDown ? TagFilter.FilterTagsControlAction.AppendIncludedFilter : TagFilter.FilterTagsControlAction.Set);
                });
            }
        }


        private RelayCommand? _cmdTagRemove;
        public RelayCommand CmdTagRemove
        {
            get
            {
                return _cmdTagRemove ??= new RelayCommand((o) =>
                {
                    FilterTagsControl(o, TagFilter.FilterTagsControlAction.Remove);
                });
            }
        }


        private RelayCommand? _cmdTagAddExcluded;
        public RelayCommand CmdTagAddExcluded
        {
            get
            {
                return _cmdTagAddExcluded ??= new RelayCommand((o) =>
                {
                    FilterTagsControl(o, TagFilter.FilterTagsControlAction.AppendExcludedFilter);
                });
            }
        }
        #endregion
    }
}
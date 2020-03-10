﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Drawing.Views;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Graphing;
using UnityEditor.Graphing.Util;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEditor.ShaderGraph.Internal;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Drawing.Inspector
{
    // Interface that should be implemented by any property drawer for the inspector view
    interface IPropertyDrawer
    {
        PropertyRow HandleProperty(PropertyInfo propertyInfo, object actualObject, Inspectable attribute);
    }

    [SGPropertyDrawer(typeof(Enum))]
    class EnumPropertyDrawer : IPropertyDrawer
    {
        private delegate void EnumValueSetter(Enum newValue);

        private PropertyRow CreatePropertyRowForField(EnumValueSetter valueChangedCallback, Enum fieldToDraw, string labelName, Enum defaultValue)
        {
            var row = new PropertyRow(new Label(labelName));
            row.Add(new EnumField(defaultValue), (field) =>
            {
                field.value = fieldToDraw;
                field.RegisterValueChangedCallback(evt => valueChangedCallback(evt.newValue));
            });

            return row;
        }

        public PropertyRow HandleProperty(PropertyInfo propertyInfo, object actualObject, Inspectable attribute)
        {
            var newPropertyRow = this.CreatePropertyRowForField(newEnumValue =>
                propertyInfo.GetSetMethod(true).Invoke(actualObject, new object[] {newEnumValue}),
                (Enum) propertyInfo.GetValue(actualObject),
                attribute.LabelName,
                (Enum) attribute.DefaultValue);

            return newPropertyRow;
        }
    }

    [SGPropertyDrawer(typeof(ToggleData))]
    class BoolPropertyDrawer : IPropertyDrawer
    {
        private delegate void BoolValueSetter(ToggleData newValue);

        private PropertyRow CreatePropertyRowForField(BoolValueSetter valueChangedCallback, ToggleData fieldToDraw, string labelName)
        {
            var row = new PropertyRow(new Label(labelName));
            row.Add(new Toggle(), (toggle) =>
            {
                toggle.value = fieldToDraw.isOn;
                toggle.OnToggleChanged(evt => valueChangedCallback(new ToggleData(evt.newValue)));
            });

            return row;
        }

        public PropertyRow HandleProperty(PropertyInfo propertyInfo, object actualObject, Inspectable attribute)
        {
            var newPropertyRow = this.CreatePropertyRowForField(newBoolValue =>
                propertyInfo.GetSetMethod(true).Invoke(actualObject, new object[] {newBoolValue}),
                (ToggleData) propertyInfo.GetValue(actualObject),
                attribute.LabelName);

            return newPropertyRow;
        }
    }

    class InspectorView : GraphSubWindow
    {
        // References
        GraphData m_GraphData;

        IList<Type> m_PropertyDrawerList = new List<Type>();

        protected override string windowTitle => "Inspector";
        protected override string elementName => "InspectorView";
        protected override string styleName => "InspectorView";

        // Passing both the manager and the data here is really bad
        // Inspector preview should be directly reactive to the preview manager
        public InspectorView(GraphData graphData, GraphView graphView) : base(graphView)
        {
            m_GraphData = graphData;

            // Register property drawer types here - Can we use Unity inbuilt property drawers instead?
            RegisterPropertyDrawer(typeof(BoolPropertyDrawer));
            RegisterPropertyDrawer(typeof(EnumPropertyDrawer));

            BuildView();
        }

        void BuildView()
        {
            BuildContentContainer();
        }
        void BuildContentContainer()
        {
        }


#region Content
        void BuildContent(VisualElement container)
        {
        }

        void RegisterPropertyDrawer(Type propertyDrawerType)
        {
            // #TODO: Look into the right way to warn the user that there are errors they should probably be aware of

            if(typeof(IPropertyDrawer).IsAssignableFrom(propertyDrawerType) == false)
                throw new Exception("Attempted to register a property drawer that doesn't inherit from IPropertyDrawer!");

            var customAttribute = propertyDrawerType.GetCustomAttribute<SGPropertyDrawer>();
            if(customAttribute != null)
                m_PropertyDrawerList.Add(propertyDrawerType);
            else
                throw new Exception("Attempted to register a property drawer that isn't marked up with the SGPropertyDrawer attribute!");
        }

        bool IsPropertyTypeHandled(Type typeOfProperty, out Type propertyDrawerToUse)
        {
            propertyDrawerToUse = null;

            // Check to see if a property drawer has been registered that handles this type
            foreach (var propertyDrawerType in m_PropertyDrawerList)
            {
                var typeHandledByPropertyDrawer = propertyDrawerType.GetCustomAttribute<SGPropertyDrawer>();
                // Numeric types and boolean wrapper types like ToggleData handled here
                if (typeHandledByPropertyDrawer.PropertyType == typeOfProperty)
                {
                    propertyDrawerToUse = propertyDrawerType;
                    return true;
                }
                // Enums are weird and need to be handled explicitly as done below as their runtime type isn't the same as System.Enum
                else if (typeHandledByPropertyDrawer.PropertyType == typeOfProperty.BaseType)
                {
                    propertyDrawerToUse = propertyDrawerType;
                    return true;
                }
            }

            return false;
        }

        public void UpdateSelection(List<ISelectable> selectedObjects)
        {
            // Remove current properties
            m_ContentContainer.Clear();

            if(selectedObjects.Count == 0)
            {
                SetSelectionToGraph();
                return;
            }

            if (selectedObjects.Count == 1)
            {
                var visualElement = (VisualElement) selectedObjects.First();
                subTitle = $"{visualElement}";
            }
            else if(selectedObjects.Count > 1)
            {
                subTitle = $"{selectedObjects.Count} Objects.";
            }

            //if (selection.FirstOrDefault() is IInspectable inspectable)
            //{
            //    m_ContextTitle.text = inspectable.displayName;
            //    m_PropertyContainer.Add(inspectable.GetInspectorContent());
            //}

            if(selectedObjects.FirstOrDefault() is UnityEditor.Experimental.GraphView.Edge edge)
            {
                subTitle = "(Edge)";
            }
            else if(selectedObjects.FirstOrDefault() is UnityEditor.Experimental.GraphView.Group group)
            {
                subTitle = "(Group)";
            }

            var propertySheet = new PropertySheet();
            try
            {
                foreach (var selectable in selectedObjects)
                {
                    GetPropertyInfoFromSelection(selectable, out var properties, out var dataObject);
                    if (dataObject == null)
                        continue;

                    foreach (var propertyInfo in properties)
                    {
                        var attribute = propertyInfo.GetCustomAttribute<Inspectable>();
                        if (attribute == null)
                            continue;

                        var propertyType = propertyInfo.PropertyType;

                        if (IsPropertyTypeHandled(propertyType, out var propertyDrawerTypeToUse))
                        {
                            var propertyDrawerInstance = (IPropertyDrawer)Activator.CreateInstance(propertyDrawerTypeToUse);
                            var propertyRow = propertyDrawerInstance.HandleProperty(propertyInfo, dataObject, attribute);
                            propertySheet.Add(propertyRow);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            m_ContentContainer.Add(propertySheet);
            m_ContentContainer.MarkDirtyRepaint();
        }

        // Meant to be overriden by whichever graph needs to use this inspector and fetch the appropriate property data from it as they see suitable
        // IsELECTABLE SShould implenet a function that lets it return the actual object that it represents (that object still has the reflection data)
        protected virtual void GetPropertyInfoFromSelection(ISelectable selectable, out PropertyInfo[] properties, out object dataObject)
        {
            properties = new PropertyInfo[] {};
            dataObject = null;

            if ((selectable is MaterialNodeView) == false)
                return;

            // #TODO: This is where we'd retrieve the settings object from the target?
            var nodeView = (MaterialNodeView) selectable;

            var node = nodeView.node;
            dataObject = node;
            properties = node.GetType().GetProperties();
        }

        void SetSelectionToGraph()
        {
            var graphEditorView = m_GraphView.GetFirstAncestorOfType<GraphEditorView>();
            if(graphEditorView == null)
                return;

            subTitle = $"{graphEditorView.assetName} (Graph)";

            // #TODO - Refactor, shouldn't this just be a property on the graph data object itself?
            var precisionField = new EnumField((Enum)m_GraphData.concretePrecision);
            precisionField.RegisterValueChangedCallback(evt =>
            {
                m_GraphData.owner.RegisterCompleteObjectUndo("Change Precision");
                if (m_GraphData.concretePrecision == (ConcretePrecision)evt.newValue)
                    return;

                m_GraphData.concretePrecision = (ConcretePrecision)evt.newValue;
                var nodeList = m_GraphView.Query<MaterialNodeView>().ToList();
                graphEditorView.colorManager.SetNodesDirty(nodeList);

                m_GraphData.ValidateGraph();
                graphEditorView.colorManager.UpdateNodeViews(nodeList);
                foreach (var node in m_GraphData.GetNodes<AbstractMaterialNode>())
                {
                    node.Dirty(ModificationScope.Graph);
                }
            });

            var sheet = new PropertySheet();
            sheet.Add(new PropertyRow(new Label("Precision")), (row) =>
            {
                row.Add(precisionField);
            });
            m_ContentContainer.Add(sheet);
        }
#endregion
    }
}
